export interface ProfileParamDef {
  key: string
  label: string
  type: 'number' | 'string' | 'select' | 'stringArray'
  min?: number
  max?: number
  step?: number
  options?: string[]
  defaultValue: number | string | string[]
  description: string
}

export interface ProfileDef {
  id: string
  label: string
  description: string
  color: string
  params: ProfileParamDef[]
}

export const profileDefinitions: ProfileDef[] = [
  {
    id: 'static',
    label: 'Static',
    description: 'Constant value',
    color: 'gray',
    params: [
      { key: 'value', label: 'Value', type: 'number', defaultValue: 0, description: 'Constant output value' },
    ],
  },
  {
    id: 'step',
    label: 'Step',
    description: 'Steps up/down by increment',
    color: 'blue',
    params: [
      { key: 'step', label: 'Step Size', type: 'number', step: 0.1, defaultValue: 1, description: 'Increment per step' },
      { key: 'lowLimit', label: 'Low Limit', type: 'number', defaultValue: 0, description: 'Minimum value' },
      { key: 'highLimit', label: 'High Limit', type: 'number', defaultValue: 100, description: 'Maximum value' },
      { key: 'updateMs', label: 'Update Interval (ms)', type: 'number', min: 10, defaultValue: 1000, description: 'Time between steps' },
      { key: 'direction', label: 'Direction', type: 'select', options: ['Up', 'Down'], defaultValue: 'Up', description: 'Initial direction' },
      { key: 'atLimit', label: 'At Limit', type: 'select', options: ['AutoReverse', 'Wrap', 'Clamp'], defaultValue: 'AutoReverse', description: 'Behavior when hitting limit' },
    ],
  },
  {
    id: 'ramp',
    label: 'Ramp',
    description: 'Linear sawtooth wave',
    color: 'teal',
    params: [
      { key: 'lowLimit', label: 'Low Limit', type: 'number', defaultValue: 0, description: 'Minimum value' },
      { key: 'highLimit', label: 'High Limit', type: 'number', defaultValue: 100, description: 'Maximum value' },
      { key: 'periodMs', label: 'Period (ms)', type: 'number', min: 10, defaultValue: 10000, description: 'Full cycle duration' },
      { key: 'updateMs', label: 'Update Interval (ms)', type: 'number', min: 10, defaultValue: 1000, description: 'Refresh rate' },
    ],
  },
  {
    id: 'sine',
    label: 'Sine',
    description: 'Sinusoidal wave',
    color: 'green',
    params: [
      { key: 'lowLimit', label: 'Low Limit', type: 'number', defaultValue: 0, description: 'Minimum value' },
      { key: 'highLimit', label: 'High Limit', type: 'number', defaultValue: 100, description: 'Maximum value' },
      { key: 'periodMs', label: 'Period (ms)', type: 'number', min: 10, defaultValue: 10000, description: 'Full cycle duration' },
      { key: 'phaseDeg', label: 'Phase (°)', type: 'number', defaultValue: 0, description: 'Phase offset in degrees' },
      { key: 'noisePercent', label: 'Noise %', type: 'number', min: 0, max: 100, defaultValue: 0, description: 'Additive noise amplitude' },
    ],
  },
  {
    id: 'cosine',
    label: 'Cosine',
    description: 'Cosine wave (90° phase-shifted sine)',
    color: 'lime',
    params: [
      { key: 'lowLimit', label: 'Low Limit', type: 'number', defaultValue: 0, description: 'Minimum value' },
      { key: 'highLimit', label: 'High Limit', type: 'number', defaultValue: 100, description: 'Maximum value' },
      { key: 'periodMs', label: 'Period (ms)', type: 'number', min: 10, defaultValue: 10000, description: 'Full cycle duration' },
      { key: 'noisePercent', label: 'Noise %', type: 'number', min: 0, max: 100, defaultValue: 0, description: 'Additive noise amplitude' },
    ],
  },
  {
    id: 'square',
    label: 'Square',
    description: 'Square wave with duty cycle',
    color: 'yellow',
    params: [
      { key: 'lowLimit', label: 'Low Limit', type: 'number', defaultValue: 0, description: 'Off value' },
      { key: 'highLimit', label: 'High Limit', type: 'number', defaultValue: 100, description: 'On value' },
      { key: 'periodMs', label: 'Period (ms)', type: 'number', min: 10, defaultValue: 10000, description: 'Full cycle duration' },
      { key: 'dutyPercent', label: 'Duty Cycle %', type: 'number', min: 0, max: 100, step: 1, defaultValue: 50, description: 'Percentage of period spent at High Limit' },
    ],
  },
  {
    id: 'triangle',
    label: 'Triangle',
    description: 'Triangular wave',
    color: 'orange',
    params: [
      { key: 'lowLimit', label: 'Low Limit', type: 'number', defaultValue: 0, description: 'Minimum value' },
      { key: 'highLimit', label: 'High Limit', type: 'number', defaultValue: 100, description: 'Maximum value' },
      { key: 'periodMs', label: 'Period (ms)', type: 'number', min: 10, defaultValue: 10000, description: 'Full cycle duration' },
    ],
  },
  {
    id: 'random',
    label: 'Random',
    description: 'Random value within range',
    color: 'pink',
    params: [
      { key: 'lowLimit', label: 'Low Limit', type: 'number', defaultValue: 0, description: 'Minimum value' },
      { key: 'highLimit', label: 'High Limit', type: 'number', defaultValue: 100, description: 'Maximum value' },
      { key: 'distribution', label: 'Distribution', type: 'select', options: ['Uniform', 'Normal'], defaultValue: 'Uniform', description: 'Random distribution type' },
      { key: 'seed', label: 'Seed', type: 'number', defaultValue: 0, description: 'RNG seed (0 = random seed)' },
    ],
  },
  {
    id: 'toggle',
    label: 'Toggle',
    description: 'Alternates true/false',
    color: 'grape',
    params: [
      { key: 'intervalMs', label: 'Interval (ms)', type: 'number', min: 10, defaultValue: 1000, description: 'Time between toggles' },
    ],
  },
  {
    id: 'pulse',
    label: 'Pulse',
    description: 'Pulse train with duty cycle',
    color: 'red',
    params: [
      { key: 'periodMs', label: 'Period (ms)', type: 'number', min: 10, defaultValue: 10000, description: 'Full cycle duration' },
      { key: 'dutyPercent', label: 'Duty Cycle %', type: 'number', min: 0, max: 100, step: 1, defaultValue: 50, description: 'Percentage of period spent ON' },
    ],
  },
  {
    id: 'counter',
    label: 'Counter',
    description: 'Increments and rolls over',
    color: 'cyan',
    params: [
      { key: 'step', label: 'Step Size', type: 'number', step: 0.1, defaultValue: 1, description: 'Increment per tick' },
      { key: 'rolloverAt', label: 'Rollover At', type: 'number', defaultValue: 100, description: 'Reset to 0 when reached' },
    ],
  },
  {
    id: 'clock',
    label: 'Clock',
    description: 'Current time as formatted string',
    color: 'indigo',
    params: [
      { key: 'format', label: 'Format', type: 'string', defaultValue: 'HH:mm:ss', description: 'DateTime format string' },
    ],
  },
  {
    id: 'textcycle',
    label: 'Text Cycle',
    description: 'Cycles through a list of strings',
    color: 'violet',
    params: [
      { key: 'intervalMs', label: 'Interval (ms)', type: 'number', min: 10, defaultValue: 1000, description: 'Time between cycles' },
      { key: 'values', label: 'Values', type: 'stringArray', defaultValue: [], description: 'List of strings to cycle through' },
    ],
  },
  {
    id: 'echo',
    label: 'Echo',
    description: 'Returns written value (for writable tags)',
    color: 'dark',
    params: [
      { key: 'value', label: 'Default Value', type: 'number', defaultValue: 0, description: 'Fallback when no written value exists' },
    ],
  },
]

export function getProfileDef(id: string): ProfileDef | undefined {
  return profileDefinitions.find((p) => p.id === id)
}

export function defaultSimulation(profileId: string): Record<string, unknown> {
  const def = getProfileDef(profileId)
  const sim: Record<string, unknown> = { profile: profileId }
  if (def) {
    for (const p of def.params) {
      sim[p.key] = p.defaultValue
    }
  }
  return sim
}
