import { useState } from 'react'
import { Stack, Title, Card, Group, Text, Button, Select, FileInput } from '@mantine/core'
import { IconDownload, IconUpload } from '@tabler/icons-react'
import { exportCsv, exportXlsx } from '../api/plcs'
import { api } from '../api/client'
import { notifySuccess, notifyError } from '../lib/notifications'

export default function ExportImport() {
  const [format, setFormat] = useState<string | null>('CSV (Kepware)')
  const [file, setFile] = useState<File | null>(null)
  const [loading, setLoading] = useState(false)

  const handleExport = async () => {
    try {
      if (format?.startsWith('CSV')) {
        const blob = await exportCsv()
        const url = URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url
        a.download = 'plc-fleet-export.csv'
        a.click()
        URL.revokeObjectURL(url)
      } else if (format?.startsWith('XLSX')) {
        const blob = await exportXlsx()
        const url = URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url
        a.download = 'plc-fleet-export.xlsx'
        a.click()
        URL.revokeObjectURL(url)
      } else if (format?.startsWith('JSON')) {
        const blob = await api.getBlob('/api/plcs/export/json')
        const url = URL.createObjectURL(new Blob([blob], { type: 'application/json' }))
        const a = document.createElement('a')
        a.href = url
        a.download = 'plc-fleet-export.json'
        a.click()
        URL.revokeObjectURL(url)
      }
    } catch {
      notifyError('Export Failed')
    }
  }

  const handleImport = async () => {
    if (!file) return
    setLoading(true)
    try {
      const text = await file.text()
      const isCsv = file.name.endsWith('.csv')
      if (isCsv) {
        const lines = text.trim().split('\n')
        const headers = lines[0].split(',').map(h => h.replace(/"/g, '').trim())
        const map = new Map<string, any>()
        for (let i = 1; i < lines.length; i++) {
          const vals = lines[i].split(',').map(v => v.replace(/"/g, '').trim())
          const entry: any = {}
          headers.forEach((h, idx) => { entry[h] = vals[idx] })
          const key = `${entry.Name}|${entry.Brand}`
          if (!map.has(key)) {
            map.set(key, { name: entry.Name, brand: parseInt(entry.Brand), personality: entry.Personality, tags: [] })
          }
          const plc = map.get(key)!
          if (entry.TagName) {
            plc.tags.push({
              name: entry.TagName,
              address: entry.TagAddress || '',
              dataType: entry.TagDataType || 'Int16',
              access: entry.TagAccess === 'ReadOnly' ? 0 : 1,
              description: '',
              engUnit: entry.TagEngUnit || '',
              enabled: entry.TagEnabled !== 'False' && entry.TagEnabled !== 'false',
              simulation: { profile: 'Static', value: 0, step: 1, direction: 'Up', lowLimit: 0, highLimit: 100, periodMs: 10000, updateMs: 1000, atLimit: 'AutoReverse', phaseDeg: 0, noisePercent: 0, dutyPercent: 50, distribution: 'Uniform', intervalMs: 1000, rolloverAt: 0, format: 'HH:mm:ss', values: [], seed: null, writePolicy: 'Override' },
            })
          }
        }
        await api.post('/api/plcs/import', [...map.values()])
      } else {
        const plcs = JSON.parse(text)
        const arr = Array.isArray(plcs) ? plcs : [plcs]
        const cleaned = arr.map(p => ({
          name: p.name || p.Name,
          brand: p.brand ?? p.Brand,
          personality: p.personality || p.Personality,
          description: p.description || p.Description || '',
          network: p.network || { ipAddress: '', port: 0 },
          tags: p.tags || [],
          behavior: { startupDelayMs: 0, latencyMs: 0 },
        }))
        await api.post('/api/plcs/import', cleaned)
      }
      notifySuccess('Import Successful', 'Refresh the dashboard to see changes.')
    } catch {
      notifyError('Import Failed')
    } finally {
      setLoading(false)
    }
  }

  return (
    <Stack>
      <Title order={3}>Export / Import</Title>

      <Card withBorder>
        <Text fw={500} mb="md">Export</Text>
        <Group align="flex-end">
          <Select label="Format" data={['CSV (Kepware)', 'XLSX (Excel)', 'JSON']} value={format} onChange={setFormat} />
          <Button onClick={handleExport} leftSection={<IconDownload size={14} />}>Export</Button>
        </Group>
      </Card>

      <Card withBorder>
        <Text fw={500} mb="md">Import</Text>
        <Stack>
          <Text c="dimmed" size="sm">Import PLC configurations from JSON or CSV (Kepware format) files.</Text>
          <FileInput
            accept=".json,.csv"
            value={file}
            onChange={setFile}
            placeholder="Select JSON or CSV file"
            clearable
          />
          <Button onClick={handleImport} leftSection={<IconUpload size={14} />} disabled={!file} loading={loading}>
            Import
          </Button>
        </Stack>
      </Card>
    </Stack>
  )
}
