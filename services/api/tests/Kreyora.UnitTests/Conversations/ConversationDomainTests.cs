using Kreyora.Domain.Conversations;
using Kreyora.Domain.Customers;
using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Conversations;

public sealed class ConversationDomainTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_BeginsAsNewWithAutomationOwnerAndNoUnread()
    {
        var conversation = Conversation.Start("tenant_1", "conn_1", "store_1", "identity_1", ChannelType.Instagram);

        Assert.Equal(ConversationStatus.New, conversation.Status);
        Assert.Equal(AutomationMode.Automated, conversation.AutomationMode);
        Assert.True(conversation.IsAutomationActive);
        Assert.Equal(0, conversation.UnreadCount);
        Assert.Equal("store_1", conversation.StoreId);
        Assert.Null(conversation.LastMessageAt);
    }

    [Fact]
    public void RecordInboundMessage_OutOfOrder_KeepsLatestTimestampsAndCountsEachMessage()
    {
        var conversation = Conversation.Start("tenant_1", "conn_1", null, "identity_1", ChannelType.Instagram);

        conversation.RecordInboundMessage(T0.AddMinutes(5));
        conversation.RecordInboundMessage(T0);

        Assert.Equal(2, conversation.UnreadCount);
        Assert.Equal(T0.AddMinutes(5), conversation.LastMessageAt);
        Assert.Equal(T0.AddMinutes(5), conversation.LastCustomerMessageAt);
    }

    [Theory]
    [InlineData(ConversationStatus.Resolved)]
    [InlineData(ConversationStatus.Closed)]
    public void RecordInboundMessage_ReopensResolvedOrClosedToNew(ConversationStatus terminal)
    {
        var conversation = WithStatus(terminal);

        conversation.RecordInboundMessage(T0);

        Assert.Equal(ConversationStatus.New, conversation.Status);
        Assert.Equal(1, conversation.UnreadCount);
    }

    [Fact]
    public void RecordInboundMessage_OnSpam_StaysSpamAndSilent()
    {
        var conversation = WithStatus(ConversationStatus.Spam);

        conversation.RecordInboundMessage(T0);

        Assert.Equal(ConversationStatus.Spam, conversation.Status);
        Assert.Equal(0, conversation.UnreadCount);
        Assert.Equal(T0, conversation.LastMessageAt);
    }

    [Fact]
    public void RecordCustomerRead_IsAMonotonicWatermark()
    {
        var conversation = Conversation.Start("tenant_1", "conn_1", null, "identity_1", ChannelType.Instagram);

        conversation.RecordCustomerRead(T0.AddMinutes(3));
        conversation.RecordCustomerRead(T0);

        Assert.Equal(T0.AddMinutes(3), conversation.CustomerLastReadAt);
    }

    [Fact]
    public void MarkReadByStaff_ResetsUnread()
    {
        var conversation = Conversation.Start("tenant_1", "conn_1", null, "identity_1", ChannelType.Instagram);
        conversation.RecordInboundMessage(T0);

        conversation.MarkReadByStaff();

        Assert.Equal(0, conversation.UnreadCount);
    }

    [Fact]
    public void Message_DeliveryStatus_NeverMovesBackwards_AndIgnoresInbound()
    {
        var inbound = Message.CreateInboundText("tenant_1", "conv_1", "conn_1", null, "mid_1", "hi", T0, T0);

        Assert.False(inbound.AdvanceDeliveryStatus(MessageDeliveryStatus.Read));
        Assert.Null(inbound.DeliveryStatus);
    }

    [Fact]
    public void OutboundMessage_StartsSent_AdvancesToRead_AndNeverDowngrades()
    {
        var outbound = Message.CreateOutboundText("tenant_1", "conv_1", "conn_1", MessageOrigin.Staff, "mid_out", "reply", T0, T0);

        Assert.Equal(MessageDeliveryStatus.Sent, outbound.DeliveryStatus);
        Assert.True(outbound.AdvanceDeliveryStatus(MessageDeliveryStatus.Read));
        Assert.False(outbound.AdvanceDeliveryStatus(MessageDeliveryStatus.Delivered));
        Assert.False(outbound.AdvanceDeliveryStatus(MessageDeliveryStatus.Failed));
        Assert.Equal(MessageDeliveryStatus.Read, outbound.DeliveryStatus);
    }

    [Fact]
    public void OutboundMessage_CannotOriginateFromCustomer()
    {
        Assert.Throws<ArgumentException>(() =>
            Message.CreateOutboundText("tenant_1", "conv_1", "conn_1", MessageOrigin.Customer, "mid_x", "x", T0, T0));
    }

    [Fact]
    public void Message_Redact_RemovesContentOnce()
    {
        var media = Message.CreateInboundMedia("tenant_1", "conv_1", "conn_1", null, "mid_2",
            "https://cdn.example/a.jpg", "image", "caption", T0, T0);

        Assert.True(media.Redact(T0));
        Assert.False(media.Redact(T0.AddMinutes(1)));
        Assert.Null(media.Text);
        Assert.Null(media.MediaUrl);
        Assert.Equal(T0, media.RedactedAt);
        Assert.Equal("image", media.MediaContentType);
    }

    [Fact]
    public void Reaction_LastWriteWins_ByProviderTime()
    {
        var reaction = MessageReaction.Create("tenant_1", "conn_1", "mid_biz", "igsid_c", string.Empty, isRemoved: true, T0.AddMinutes(2));

        // An older "react" arriving late must not resurrect a newer removal.
        Assert.False(reaction.Apply("❤", isRemoved: false, T0));
        Assert.True(reaction.IsRemoved);

        Assert.True(reaction.Apply("😂", isRemoved: false, T0.AddMinutes(5)));
        Assert.False(reaction.IsRemoved);
        Assert.Equal("😂", reaction.Emoji);
    }

    [Fact]
    public void Identity_Activity_NeverMovesBackwards_AndErasureClearsNameOnce()
    {
        var identity = CustomerChannelIdentity.Create("tenant_1", "conn_1", ChannelType.Instagram, "igsid_123456", T0);
        identity.RecordActivity(T0.AddMinutes(10));
        identity.RecordActivity(T0.AddMinutes(-5));
        identity.UpdateDisplayName("Sita");

        Assert.Equal(T0.AddMinutes(-5), identity.FirstSeenAt);
        Assert.Equal(T0.AddMinutes(10), identity.LastSeenAt);
        Assert.True(identity.Erase(T0));
        Assert.False(identity.Erase(T0));
        Assert.Null(identity.DisplayName);

        identity.UpdateDisplayName("Back again");
        Assert.Null(identity.DisplayName);
    }

    [Theory]
    [InlineData("igsid_123456", "Instagram user ·3456")]
    [InlineData("abc", "Instagram user ·abc")]
    public void MaskedLabel_ShowsOnlyLastFourCharacters(string externalUserId, string expected)
    {
        Assert.Equal(expected, CustomerChannelIdentity.MaskedLabel(ChannelType.Instagram, externalUserId));
    }

    [Fact]
    public void TakeOver_PausesAutomation_MovesActiveThreadToHumanAssigned_AndIsIdempotent()
    {
        var conversation = Conversation.Start("tenant_1", "conn_1", null, "identity_1", ChannelType.Instagram);

        Assert.True(conversation.TakeOver());
        Assert.False(conversation.TakeOver());
        Assert.Equal(AutomationMode.HumanTakeover, conversation.AutomationMode);
        Assert.Equal(ConversationStatus.HumanAssigned, conversation.Status);
        Assert.False(conversation.IsAutomationActive);

        Assert.True(conversation.Release());
        Assert.False(conversation.Release());
        Assert.Equal(AutomationMode.Automated, conversation.AutomationMode);
        Assert.Equal(ConversationStatus.BotActive, conversation.Status);
    }

    [Theory]
    [InlineData(ConversationStatus.Resolved)]
    [InlineData(ConversationStatus.Closed)]
    [InlineData(ConversationStatus.Spam)]
    public void TakeOverAndRelease_KeepDispositions(ConversationStatus disposition)
    {
        var conversation = WithStatus(disposition);

        conversation.TakeOver();
        Assert.Equal(disposition, conversation.Status);
        conversation.Release();
        Assert.Equal(disposition, conversation.Status);
    }

    [Theory]
    [InlineData(ConversationStatus.New, ConversationStatusAction.Resolve, ConversationStatus.Resolved)]
    [InlineData(ConversationStatus.HumanAssigned, ConversationStatusAction.Close, ConversationStatus.Closed)]
    [InlineData(ConversationStatus.Resolved, ConversationStatusAction.Reopen, ConversationStatus.New)]
    [InlineData(ConversationStatus.Closed, ConversationStatusAction.Reopen, ConversationStatus.New)]
    [InlineData(ConversationStatus.BotActive, ConversationStatusAction.MarkSpam, ConversationStatus.Spam)]
    [InlineData(ConversationStatus.Spam, ConversationStatusAction.UnmarkSpam, ConversationStatus.New)]
    public void StatusActions_FollowAdr017(ConversationStatus from, ConversationStatusAction action, ConversationStatus to)
    {
        var conversation = WithStatus(from);

        Assert.True(conversation.ApplyStatusAction(action));
        Assert.Equal(to, conversation.Status);
    }

    [Theory]
    [InlineData(ConversationStatus.New, ConversationStatusAction.Reopen)]
    [InlineData(ConversationStatus.Spam, ConversationStatusAction.Resolve)]
    [InlineData(ConversationStatus.Spam, ConversationStatusAction.Close)]
    [InlineData(ConversationStatus.New, ConversationStatusAction.UnmarkSpam)]
    public void StatusActions_RejectInvalidTransitions(ConversationStatus from, ConversationStatusAction action)
    {
        var conversation = WithStatus(from);

        Assert.Throws<InvalidOperationException>(() => conversation.ApplyStatusAction(action));
        Assert.Equal(from, conversation.Status);
    }

    [Fact]
    public void StatusAction_SameTarget_IsNoOp()
    {
        var conversation = WithStatus(ConversationStatus.Resolved);

        Assert.False(conversation.ApplyStatusAction(ConversationStatusAction.Resolve));
    }

    [Fact]
    public void PendingStaffReply_AcceptsOnce_ThenFailureCannotOverrideSuccess()
    {
        var reply = Message.CreatePendingStaffReply("tenant_1", "conv_1", "conn_1", "out_1", "user_1", "On it!", T0);

        Assert.True(reply.IsPending);
        Assert.Null(reply.ProviderMessageId);
        reply.RecordProviderAcceptance("mid_sent", T0.AddSeconds(2));
        Assert.False(reply.IsPending);
        Assert.Equal(MessageDeliveryStatus.Sent, reply.DeliveryStatus);
        Assert.False(reply.MarkDeliveryFailed());
        Assert.Equal("user_1", reply.ActorUserId);
        Assert.Equal(MessageOrigin.Staff, reply.Origin);
    }

    [Fact]
    public void PendingStaffReply_PermanentFailure_IsVisible()
    {
        var reply = Message.CreatePendingStaffReply("tenant_1", "conv_1", "conn_1", "out_1", "user_1", "On it!", T0);

        Assert.True(reply.MarkDeliveryFailed());
        Assert.Equal(MessageDeliveryStatus.Failed, reply.DeliveryStatus);
    }

    [Fact]
    public void ProviderNativeEcho_IsOutboundAndSent()
    {
        var echo = Message.CreateProviderNativeEcho("tenant_1", "conv_1", "conn_1", null, "mid_echo", "typed in app", null, null, T0, T0);

        Assert.Equal(MessageDirection.Outbound, echo.Direction);
        Assert.Equal(MessageOrigin.ProviderNative, echo.Origin);
        Assert.Equal(MessageDeliveryStatus.Sent, echo.DeliveryStatus);
    }

    private static Conversation WithStatus(ConversationStatus status)
    {
        var conversation = Conversation.Start("tenant_1", "conn_1", null, "identity_1", ChannelType.Instagram);
        typeof(Conversation).GetProperty(nameof(Conversation.Status))!.SetValue(conversation, status);
        return conversation;
    }
}
