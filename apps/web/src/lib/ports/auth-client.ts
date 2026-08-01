export interface AuthenticatedUser {
  id: string;
  displayName: string;
  email: string;
}

export interface RegisterOwnerInput {
  displayName: string;
  email: string;
  password: string;
  tenantDisplayName: string;
  tenantSlug: string;
}

export interface AuthClient {
  register(input: RegisterOwnerInput): Promise<void>;
  signIn(email: string, password: string): Promise<void>;
  signOut(): Promise<void>;
  getCurrentUser(): Promise<AuthenticatedUser>;
  requestPasswordReset(email: string): Promise<string | null>;
  resetPassword(email: string, token: string, newPassword: string): Promise<void>;
}
