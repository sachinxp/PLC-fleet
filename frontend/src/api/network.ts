import { api } from './client'

export interface NetworkStatus {
  isElevated: boolean
  message: string
}

export function getNics(): Promise<string[]> {
  return api.get<string[]>('/api/network/nics')
}

export function getStatus(): Promise<NetworkStatus> {
  return api.get<NetworkStatus>('/api/network/status')
}
