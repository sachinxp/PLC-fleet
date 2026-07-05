export const Brand = {
  Siemens: 0,
  Rockwell: 1,
  Modbus: 2,
  Mitsubishi: 3,
  Beckhoff: 4,
  OpcUa: 5,
} as const
export type Brand = (typeof Brand)[keyof typeof Brand]

export const PlcState = {
  Created: 0,
  Running: 1,
  Stopped: 2,
  Error: 3,
} as const
export type PlcState = (typeof PlcState)[keyof typeof PlcState]

export const TagAccess = {
  ReadOnly: 0,
  ReadWrite: 1,
} as const
export type TagAccess = (typeof TagAccess)[keyof typeof TagAccess]

export interface SimulationConfig {
  profile: string
  value: number
  step: number
  direction: string
  lowLimit: number
  highLimit: number
  periodMs: number
  updateMs: number
  atLimit: string
  phaseDeg: number
  noisePercent: number
  dutyPercent: number
  distribution: string
  intervalMs: number
  rolloverAt: number
  format: string
  values: string[]
  seed: number | null
  writePolicy: string
}

export interface TagDefinition {
  name: string
  address: string
  dataType: string
  access: TagAccess
  description: string
  engUnit: string
  enabled: boolean
  simulation: SimulationConfig
}

export interface NetworkConfig {
  ipAddress: string
  port: number
  nicName: string
}

export interface BehaviorConfig {
  startupDelayMs: number
  latencyMs: number
}

export interface PlcInstance {
  id: string
  name: string
  brand: Brand
  personality: string
  description: string
  state: PlcState
  network: NetworkConfig
  behavior: BehaviorConfig
  tags: TagDefinition[]
  orderCode: string
  serialNumber: string
  firmwareVersion: string
  activeConnections: number
  requestsServed: number
  errorCount: number
}

export const brandNames: Record<Brand, string> = {
  [Brand.Siemens]: 'Siemens',
  [Brand.Rockwell]: 'Rockwell',
  [Brand.Modbus]: 'Modbus',
  [Brand.Mitsubishi]: 'Mitsubishi',
  [Brand.Beckhoff]: 'Beckhoff',
  [Brand.OpcUa]: 'OPC UA',
}

export const brandColors: Record<Brand, string> = {
  [Brand.Siemens]: 'blue',
  [Brand.Rockwell]: 'red',
  [Brand.Modbus]: 'green',
  [Brand.Mitsubishi]: 'orange',
  [Brand.Beckhoff]: 'violet',
  [Brand.OpcUa]: 'cyan',
}

export const stateLabels: Record<PlcState, string> = {
  [PlcState.Created]: 'Created',
  [PlcState.Running]: 'Running',
  [PlcState.Stopped]: 'Stopped',
  [PlcState.Error]: 'Error',
}

export const stateColors: Record<PlcState, string> = {
  [PlcState.Created]: 'gray',
  [PlcState.Running]: 'green',
  [PlcState.Stopped]: 'orange',
  [PlcState.Error]: 'red',
}

export const dataTypeOptions = ['Bool', 'Int16', 'Int32', 'UInt16', 'UInt32', 'Float32', 'Float64', 'String'] as const
