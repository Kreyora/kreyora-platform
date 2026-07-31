import { apiFetch } from "@/lib/api/api-client";

export interface SystemInfo {
  name: string;
  version: string;
  environment: string;
}

export const apiSystemClient = {
  async getInfo(): Promise<SystemInfo> {
    return apiFetch<SystemInfo>("/v1/system/info");
  },
};
