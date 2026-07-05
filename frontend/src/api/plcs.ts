import type { PlcInstance, Brand } from '../types'
import { api } from './client'

export interface CreatePlcRequest {
  name: string
  brand: Brand
  personality: string
  description?: string
  ipAddress?: string
  maxConnections?: number
}

export function getAll(): Promise<PlcInstance[]> {
  return api.get<PlcInstance[]>('/api/plcs')
}

export function getById(id: string): Promise<PlcInstance> {
  return api.get<PlcInstance>(`/api/plcs/${id}`)
}

export function create(req: CreatePlcRequest): Promise<PlcInstance> {
  return api.post<PlcInstance>('/api/plcs', req)
}

export function update(id: string, plc: PlcInstance): Promise<PlcInstance> {
  return api.put<PlcInstance>(`/api/plcs/${id}`, plc)
}

export function remove(id: string): Promise<void> {
  return api.del(`/api/plcs/${id}`)
}

export function start(id: string): Promise<void> {
  return api.post<void>(`/api/plcs/${id}/start`)
}

export function stop(id: string): Promise<void> {
  return api.post<void>(`/api/plcs/${id}/stop`)
}

export function exportCsv(): Promise<Blob> {
  return api.getBlob('/api/plcs/export/csv')
}

export function exportXlsx(): Promise<Blob> {
  return api.getBlob('/api/plcs/export/xlsx')
}
