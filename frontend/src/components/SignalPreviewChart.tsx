import { useEffect, useState } from 'react'
import { Paper, Text } from '@mantine/core'
import { LineChart, Line, XAxis, YAxis, CartesianGrid, ResponsiveContainer } from 'recharts'

interface DataPoint {
  t: number
  v: number
}

interface Props {
  profile: string
  params: Record<string, unknown>
  dataType?: string
}

function computeSimulatedValue(profile: string, params: Record<string, unknown>, elapsedMs: number): number {
  const periodMs = (params.periodMs as number) || 10000
  const lowLimit = (params.lowLimit as number) ?? 0
  const highLimit = (params.highLimit as number) ?? 100
  const range = highLimit - lowLimit

  switch (profile) {
    case 'static':
      return (params.value as number) ?? 0

    case 'step': {
      const step = (params.step as number) ?? 1
      const dir = (params.direction as string) ?? 'Up'
      const atLimit = (params.atLimit as string) ?? 'AutoReverse'
      const updateMs = (params.updateMs as number) ?? 1000
      const steps = Math.floor(elapsedMs / updateMs)
      if (dir === 'Down') {
        if (atLimit === 'AutoReverse') {
          const full = Math.floor(steps / (range / step))
          return full % 2 === 0 ? highLimit - (steps * step) % (range) : lowLimit + (steps * step) % (range)
        }
        return highLimit - (steps * step) % (range + step)
      }
      if (atLimit === 'AutoReverse') {
        const full = Math.floor(steps / (range / step))
        return full % 2 === 0 ? lowLimit + (steps * step) % (range) : highLimit - (steps * step) % (range)
      }
      return lowLimit + (steps * step) % (range + step)
    }

    case 'ramp': {
      const progress = (elapsedMs % periodMs) / periodMs
      return lowLimit + progress * range
    }

    case 'sine':
    case 'cosine': {
      const phaseDeg = (params.phaseDeg as number) ?? 0
      const noise = (params.noisePercent as number) ?? 0
      const radians = (elapsedMs % periodMs) / periodMs * Math.PI * 2
      const phaseRad = phaseDeg * Math.PI / 180
      const offset = profile === 'cosine' ? Math.PI / 2 : 0
      const mid = (highLimit + lowLimit) / 2
      const amp = range / 2
      let val = mid + amp * Math.sin(radians + phaseRad + offset)
      if (noise > 0) {
        const n = (Math.sin(elapsedMs * 0.001 * 13.37) * 0.5 + Math.sin(elapsedMs * 0.001 * 7.11) * 0.5) * noise / 100 * amp
        val += n
      }
      return val
    }

    case 'square': {
      const duty = ((params.dutyPercent as number) ?? 50) / 100
      const pos = (elapsedMs % periodMs) / periodMs
      return pos < duty ? highLimit : lowLimit
    }

    case 'triangle': {
      const t = (elapsedMs % periodMs) / periodMs
      return t < 0.5 ? lowLimit + t * 2 * range : highLimit - (t - 0.5) * 2 * range
    }

    case 'random': {
      const seed = (params.seed as number) ?? 0
      const h = Math.sin((elapsedMs / 1000) * 12.9898 + seed * 78.233) * 43758.5453
      const r = h - Math.floor(h)
      return lowLimit + r * range
    }

    case 'counter': {
      const step = (params.step as number) ?? 1
      const rollover = (params.rolloverAt as number) ?? 100
      const ticks = Math.floor(elapsedMs / 1000)
      return (ticks * step) % rollover
    }

    case 'toggle': {
      const intervalMs = (params.intervalMs as number) ?? 1000
      return Math.floor(elapsedMs / intervalMs) % 2 === 0 ? 0 : 1
    }

    case 'pulse': {
      const duty = ((params.dutyPercent as number) ?? 50) / 100
      const pos2 = (elapsedMs % periodMs) / periodMs
      return pos2 < duty ? 1 : 0
    }

    default:
      return 0
  }
}

export default function SignalPreviewChart({ profile, params, dataType }: Props) {
  const [data, setData] = useState<DataPoint[]>([])

  useEffect(() => {
    const periodMs = (params.periodMs as number) ?? 10000
    const duration = Math.max(periodMs, 5000)
    const points = 200
    const generated: DataPoint[] = []
    for (let i = 0; i < points; i++) {
      const t = (i / points) * duration
      generated.push({ t, v: computeSimulatedValue(profile, params, t) })
    }
    setData(generated)
  }, [profile, params, dataType])

  if (data.length === 0) return null

  return (
    <Paper p="sm" withBorder>
      <Text size="sm" fw={600} mb="xs">Signal Preview</Text>
      <ResponsiveContainer width="100%" height={180}>
        <LineChart data={data} margin={{ top: 5, right: 5, bottom: 5, left: 0 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#333" />
          <XAxis dataKey="t" hide />
          <YAxis domain={['auto', 'auto']} width={45} tick={{ fontSize: 10 }} />
          <Line type="monotone" dataKey="v" stroke="#228be6" dot={false} strokeWidth={1.5} />
        </LineChart>
      </ResponsiveContainer>
    </Paper>
  )
}
