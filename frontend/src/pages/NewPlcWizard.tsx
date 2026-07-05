import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Stack, Title, TextInput, Select, Button, Group, Card, Text, Code, NumberInput } from '@mantine/core'
import * as plcsApi from '../api/plcs'
import { Brand } from '../types'

const brands = [
  { value: '0', label: 'Siemens' },
  { value: '1', label: 'Rockwell' },
  { value: '2', label: 'Modbus' },
  { value: '3', label: 'Mitsubishi' },
  { value: '4', label: 'Beckhoff' },
  { value: '5', label: 'OPC UA' },
]

const personalities: Record<string, { value: string; label: string }[]> = {
  '0': [{ value: 's7-300', label: 'S7-300' }, { value: 's7-400', label: 'S7-400' }, { value: 's7-1200', label: 'S7-1200' }, { value: 's7-1500', label: 'S7-1500' }],
  '1': [{ value: 'controllogix-l7x', label: 'ControlLogix 1756-L7x' }, { value: 'compactlogix-l33er', label: 'CompactLogix 1769-L33ER' }],
  '2': [{ value: 'generic', label: 'Generic' }, { value: 'm340', label: 'Schneider M340' }],
  '3': [{ value: 'fx5u', label: 'FX5U' }, { value: 'q03ude', label: 'Q03UDE' }],
  '4': [{ value: 'twincat-3', label: 'TwinCAT 3' }, { value: 'twincat-2', label: 'TwinCAT 2' }],
  '5': [{ value: 'generic-server', label: 'Generic Server' }, { value: 's7-1500-flavored', label: 'S7-1500 Flavored' }],
}

const brandIpSuggestions: Record<string, string> = {
  '0': '192.168.0.1',
  '1': '192.168.1.10',
  '2': '192.168.0.10',
  '3': '192.168.3.250',
  '4': '192.168.0.201',
  '5': '127.0.0.1',
}

export default function NewPlcWizard() {
  const navigate = useNavigate()
  const [step, setStep] = useState(0)
  const [name, setName] = useState('')
  const [brand, setBrand] = useState('0')
  const [personality, setPersonality] = useState('s7-300')
  const [description, setDescription] = useState('')
  const [ipAddress, setIpAddress] = useState('')
  const [maxConnections, setMaxConnections] = useState(8)

  const handleBrandChange = (v: string | null) => {
    const b = v ?? '0'
    setBrand(b)
    const p = personalities[b]
    if (p) setPersonality(p[0].value)
  }

  const create = async () => {
    const plc = await plcsApi.create({
      name,
      brand: parseInt(brand) as Brand,
      personality,
      description,
      ipAddress: ipAddress || undefined,
      maxConnections,
    })
    navigate(`/plcs/${plc.id}`)
  }

  return (
    <Stack maw={600}>
      <Title order={3}>New PLC</Title>
      <Card withBorder>
        {step === 0 && (
          <Stack>
            <Text fw={500}>Step 1: Basic Info</Text>
            <TextInput label="PLC Name" value={name} onChange={e => setName(e.target.value)} required placeholder="MyS7PLC" />
            <Select label="Brand" data={brands} value={brand} onChange={handleBrandChange} />
            <Select label="Personality" data={personalities[brand] ?? []} value={personality} onChange={v => setPersonality(v ?? '')} />
            <TextInput label="Description" value={description} onChange={e => setDescription(e.target.value)} />
            <Group justify="space-between">
              <Button variant="light" onClick={() => navigate('/')}>Cancel</Button>
              <Button onClick={() => setStep(1)} disabled={!name}>Next</Button>
            </Group>
          </Stack>
        )}
        {step === 1 && (
          <Stack>
            <Text fw={500}>Step 2: Network</Text>
            <TextInput label="IP Address" value={ipAddress} onChange={e => setIpAddress(e.target.value)} placeholder={`${brandIpSuggestions[brand]} (auto-assign if empty)`} description={`Suggestion: ${brandIpSuggestions[brand]}`} />
            <NumberInput label="Max Connections" value={maxConnections} onChange={v => setMaxConnections(Number(v) || 8)} min={1} max={64} />
            <Group justify="space-between">
              <Button variant="light" onClick={() => setStep(0)}>Back</Button>
              <Button onClick={() => setStep(2)}>Next</Button>
            </Group>
          </Stack>
        )}
        {step === 2 && (
          <Stack>
            <Text fw={500}>Step 3: Review</Text>
            <Card withBorder p="sm" bg="dark.7">
              <Stack gap="xs">
                <Text size="sm"><Code>Name:</Code> {name}</Text>
                <Text size="sm"><Code>Brand:</Code> {brands[parseInt(brand)]?.label}</Text>
                <Text size="sm"><Code>Model:</Code> {personality}</Text>
                <Text size="sm"><Code>IP:</Code> {ipAddress || '(auto-assign)'}</Text>
                <Text size="sm"><Code>Connections:</Code> {maxConnections}</Text>
              </Stack>
            </Card>
            <Group justify="space-between">
              <Button variant="light" onClick={() => setStep(1)}>Back</Button>
              <Button onClick={create}>Create PLC</Button>
            </Group>
          </Stack>
        )}
      </Card>
    </Stack>
  )
}
