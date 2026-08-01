import type { AuthClient, AuthenticatedUser, RegisterOwnerInput } from "@/lib/ports/auth-client";

const fixtureUser: AuthenticatedUser = { id: "demo-user", displayName: "Demo Seller", email: "demo@kreyora.test" };

export const mockAuthClient: AuthClient = {
  async register(_input: RegisterOwnerInput) {},
  async signIn(_email: string, _password: string) {},
  async signOut() {},
  async getCurrentUser() { return fixtureUser; },
  async requestPasswordReset(_email: string) {},
  async resetPassword(_email: string, _token: string, _newPassword: string) {},
};
