import { create } from 'zustand'
import type { PlcInstance } from '../types'
import * as plcsApi from '../api/plcs'

interface FleetState {
  plcs: PlcInstance[]
  loading: boolean
  error: string | null
  load: () => Promise<void>
  upsertPlc: (plc: PlcInstance) => void
  removePlc: (id: string) => void
  updateState: (id: string, state: number) => void
}

export const useFleetStore = create<FleetState>((set, get) => ({
  plcs: [],
  loading: false,
  error: null,

  load: async () => {
    set({ loading: true, error: null })
    try {
      const plcs = await plcsApi.getAll()
      set({ plcs, loading: false })
    } catch (e) {
      set({ error: (e as Error).message, loading: false })
    }
  },

  upsertPlc: (plc) => {
    const plcs = get().plcs
    const idx = plcs.findIndex(p => p.id === plc.id)
    if (idx >= 0) {
      plcs[idx] = plc
      set({ plcs: [...plcs] })
    } else {
      set({ plcs: [...plcs, plc] })
    }
  },

  removePlc: (id) => {
    set({ plcs: get().plcs.filter(p => p.id !== id) })
  },

  updateState: (id, state) => {
    set({
      plcs: get().plcs.map(p => p.id === id ? { ...p, state } : p),
    })
  },
}))
