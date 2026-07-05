import { useEffect, useRef } from 'react'
import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
} from '@microsoft/signalr'
import { useFleetStore } from '../stores/fleetStore'

const HUB_URL = 'http://localhost:5000/hubs/fleet'

export function useSignalR() {
  const connectionRef = useRef<HubConnection | null>(null)
  const upsertPlc = useFleetStore(s => s.upsertPlc)
  const removePlc = useFleetStore(s => s.removePlc)
  const updateState = useFleetStore(s => s.updateState)

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('PlcCreated', (plc: any) => {
      upsertPlc(plc)
    })

    connection.on('PlcDeleted', ({ id }: { id: string }) => {
      removePlc(id)
    })

    connection.on('PlcStateChanged', ({ id, state }: { id: string; state: string }) => {
      const stateMap: Record<string, number> = { Created: 0, Running: 1, Stopped: 2, Error: 3 }
      updateState(id, stateMap[state] ?? 0)
    })

    connection.onreconnecting((error) => {
      console.warn('SignalR reconnecting...', error)
    })

    connection.onclose((error) => {
      console.warn('SignalR closed', error)
    })

    connection.start()
      .then(() => connection.invoke('JoinFleet'))
      .catch(console.error)

    connectionRef.current = connection

    return () => {
      connection.invoke('LeaveFleet')
        .then(() => connection.stop())
        .catch(() => connection.stop())
    }
  }, [upsertPlc, removePlc, updateState])

  return connectionRef
}
