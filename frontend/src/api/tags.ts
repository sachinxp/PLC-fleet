import type { TagDefinition } from '../types'
import { api } from './client'

export interface TagPatch {
  name?: string
  address?: string
  dataType?: string
  access?: number
  description?: string
  engUnit?: string
  enabled?: boolean
}

export function getAll(plcId: string): Promise<TagDefinition[]> {
  return api.get<TagDefinition[]>(`/api/plcs/${plcId}/tags`)
}

export function create(plcId: string, tag: TagDefinition): Promise<TagDefinition> {
  return api.post<TagDefinition>(`/api/plcs/${plcId}/tags`, tag)
}

export function update(plcId: string, tagName: string, tag: TagDefinition): Promise<TagDefinition> {
  return api.put<TagDefinition>(`/api/plcs/${plcId}/tags/${encodeURIComponent(tagName)}`, tag)
}

export function patch(plcId: string, tagName: string, patchData: TagPatch): Promise<TagDefinition> {
  return api.patch<TagDefinition>(`/api/plcs/${plcId}/tags/${encodeURIComponent(tagName)}`, patchData)
}

export function remove(plcId: string, tagName: string): Promise<void> {
  return api.del(`/api/plcs/${plcId}/tags/${encodeURIComponent(tagName)}`)
}
