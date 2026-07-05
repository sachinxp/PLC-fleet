import { api } from './client'

export interface SystemInfo {
  version: string
  dotnetVersion: string
  os: string
}

export function getInfo(): Promise<SystemInfo> {
  return api.get<SystemInfo>('/api/system/info')
}
