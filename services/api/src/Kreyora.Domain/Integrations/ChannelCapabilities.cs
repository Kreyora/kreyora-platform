namespace Kreyora.Domain.Integrations;

public sealed record ChannelCapabilities(
    bool CanReceiveText,
    bool CanReceiveMedia,
    bool CanSendText,
    bool CanSendMedia,
    bool CanSendLinkPreview,
    bool RequiresTemplatesOutsideWindow,
    bool SupportsReactions,
    bool SupportsDeliveryReceipts,
    bool SupportsReadReceipts,
    bool Enforces24HourWindow,
    bool SupportsTokenRefresh,
    bool RequiresSignatureVerification)
{
    public static ChannelCapabilities WhatsAppCloudApi() => new(
        CanReceiveText: true,
        CanReceiveMedia: true,
        CanSendText: true,
        CanSendMedia: true,
        CanSendLinkPreview: true,
        RequiresTemplatesOutsideWindow: true,
        SupportsReactions: true,
        SupportsDeliveryReceipts: true,
        SupportsReadReceipts: true,
        Enforces24HourWindow: true,
        SupportsTokenRefresh: true,
        RequiresSignatureVerification: true);

    public static ChannelCapabilities InstagramGraphApi() => new(
        CanReceiveText: true,
        CanReceiveMedia: true,
        CanSendText: true,
        CanSendMedia: true,
        CanSendLinkPreview: true,
        RequiresTemplatesOutsideWindow: false,
        SupportsReactions: true,
        SupportsDeliveryReceipts: false,
        SupportsReadReceipts: true,
        Enforces24HourWindow: true,
        SupportsTokenRefresh: true,
        RequiresSignatureVerification: true);

    public static ChannelCapabilities MessengerPlatform() => new(
        CanReceiveText: true,
        CanReceiveMedia: true,
        CanSendText: true,
        CanSendMedia: true,
        CanSendLinkPreview: true,
        RequiresTemplatesOutsideWindow: false,
        SupportsReactions: true,
        SupportsDeliveryReceipts: true,
        SupportsReadReceipts: true,
        Enforces24HourWindow: true,
        SupportsTokenRefresh: true,
        RequiresSignatureVerification: true);

    public static ChannelCapabilities ViberBotApi() => new(
        CanReceiveText: true,
        CanReceiveMedia: true,
        CanSendText: true,
        CanSendMedia: true,
        CanSendLinkPreview: true,
        RequiresTemplatesOutsideWindow: false,
        SupportsReactions: false,
        SupportsDeliveryReceipts: true,
        SupportsReadReceipts: true,
        Enforces24HourWindow: false,
        SupportsTokenRefresh: false,
        RequiresSignatureVerification: true);

    public static ChannelCapabilities TelegramBotApi() => new(
        CanReceiveText: true,
        CanReceiveMedia: true,
        CanSendText: true,
        CanSendMedia: true,
        CanSendLinkPreview: true,
        RequiresTemplatesOutsideWindow: false,
        SupportsReactions: true,
        SupportsDeliveryReceipts: false,
        SupportsReadReceipts: false,
        Enforces24HourWindow: false,
        SupportsTokenRefresh: false,
        RequiresSignatureVerification: true);

    public static ChannelCapabilities FullSimulator() => new(
        CanReceiveText: true,
        CanReceiveMedia: true,
        CanSendText: true,
        CanSendMedia: true,
        CanSendLinkPreview: true,
        RequiresTemplatesOutsideWindow: true,
        SupportsReactions: true,
        SupportsDeliveryReceipts: true,
        SupportsReadReceipts: true,
        Enforces24HourWindow: true,
        SupportsTokenRefresh: true,
        RequiresSignatureVerification: true);

    public static ChannelCapabilities ForChannel(ChannelType channel) => channel switch
    {
        ChannelType.WhatsApp => WhatsAppCloudApi(),
        ChannelType.Instagram => InstagramGraphApi(),
        ChannelType.Messenger => MessengerPlatform(),
        ChannelType.Viber => ViberBotApi(),
        ChannelType.Telegram => TelegramBotApi(),
        ChannelType.Simulator => FullSimulator(),
        _ => throw new ArgumentOutOfRangeException(nameof(channel), $"Unsupported channel type: {channel}")
    };
}

