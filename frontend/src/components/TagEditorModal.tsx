import { useState, useEffect, useMemo } from 'react'
import { Stack, Group, TextInput, Select, Switch, Button, Text, ActionIcon, NumberInput } from '@mantine/core'
import { IconPlus, IconTrash } from '@tabler/icons-react'
import type { TagDefinition, TagAccess } from '../types'
import { dataTypeOptions } from '../types'
import { profileDefinitions, getProfileDef, defaultSimulation } from '../types/profiles'
import SignalPreviewChart from './SignalPreviewChart'

interface Props {
  opened: boolean
  onClose: () => void
  onSave: (tag: TagDefinition) => Promise<void>
  plcId: string
  initialTag?: TagDefinition | null
}

export default function TagEditorModal({ opened, onClose, onSave, initialTag }: Props) {
  const isEdit = !!initialTag
  const [name, setName] = useState('')
  const [address, setAddress] = useState('')
  const [dataType, setDataType] = useState('Int16')
  const [description, setDescription] = useState('')
  const [engUnit, setEngUnit] = useState('')
  const [enabled, setEnabled] = useState(true)
  const [access, setAccess] = useState<string>('0')
  const [profile, setProfile] = useState('static')
  const [profileParams, setProfileParams] = useState<Record<string, unknown>>({})

  useEffect(() => {
    if (initialTag) {
      setName(initialTag.name)
      setAddress(initialTag.address)
      setDataType(initialTag.dataType)
      setDescription(initialTag.description ?? '')
      setEngUnit(initialTag.engUnit ?? '')
      setEnabled(initialTag.enabled)
      setAccess(String(initialTag.access ?? 0))
      const sim = initialTag.simulation
      setProfile(sim.profile)
      const params: Record<string, unknown> = {}
      const def = getProfileDef(sim.profile)
      if (def) {
        for (const p of def.params) {
          params[p.key] = (sim as unknown as Record<string, unknown>)[p.key] ?? p.defaultValue
        }
      }
      setProfileParams(params)
    } else {
      resetForm()
    }
  }, [initialTag, opened])

  const resetForm = () => {
    setName('')
    setAddress('')
    setDataType('Int16')
    setDescription('')
    setEngUnit('')
    setEnabled(true)
    setAccess('0')
    setProfile('static')
    setProfileParams(defaultSimulation('static') as Record<string, unknown>)
  }

  const profileDef = useMemo(() => getProfileDef(profile), [profile])

  const updateParam = (key: string, value: unknown) => {
    setProfileParams((prev) => ({ ...prev, [key]: value }))
  }

  const handleSave = async () => {
    const sim: Record<string, unknown> = { profile, ...profileParams }
    const tag: TagDefinition = {
      name,
      address,
      dataType,
      access: Number(access) as TagAccess,
      description,
      engUnit,
      enabled,
      simulation: sim as unknown as TagDefinition['simulation'],
    }
    await onSave(tag)
    onClose()
  }

  const profileParamFields = useMemo(() => {
    if (!profileDef) return null
    return profileDef.params.map((p) => {
      const value = profileParams[p.key] ?? p.defaultValue
      if (p.type === 'number') {
        return (
          <NumberInput
            key={p.key}
            label={p.label}
            description={p.description}
            value={value as number}
            onChange={(v) => updateParam(p.key, v ?? 0)}
            min={p.min}
            max={p.max}
            decimalScale={p.step !== undefined && p.step < 1 ? 2 : 0}
          />
        )
      }
      if (p.type === 'string') {
        return (
          <TextInput
            key={p.key}
            label={p.label}
            description={p.description}
            value={value as string}
            onChange={(e) => updateParam(p.key, e.target.value)}
          />
        )
      }
      if (p.type === 'select') {
        const opts = p.options ?? []
        return (
          <Select
            key={p.key}
            label={p.label}
            description={p.description}
            data={opts}
            value={value as string}
            onChange={(v) => updateParam(p.key, v ?? opts[0] ?? '')}
          />
        )
      }
      if (p.type === 'stringArray') {
        return (
          <Stack key={p.key} gap={4}>
            <Text size="sm" fw={500}>{p.label}</Text>
            <Text size="xs" c="dimmed">{p.description}</Text>
            <StringArrayEditor values={value as string[]} onChange={(v) => updateParam(p.key, v)} />
          </Stack>
        )
      }
      return null
    })
  }, [profileDef, profileParams])

  return (
    <Stack gap="sm">
      <Group grow>
        <TextInput label="Name" value={name} onChange={(e) => setName(e.target.value)} required />
        <TextInput label="Address" value={address} onChange={(e) => setAddress(e.target.value)} required />
      </Group>
      <Group grow>
        <Select label="Data Type" data={[...dataTypeOptions]} value={dataType} onChange={(v) => setDataType(v ?? 'Int16')} />
        <Select label="Access" data={[{ value: '0', label: 'Read Only' }, { value: '1', label: 'Read/Write' }]} value={access} onChange={(v) => setAccess(v ?? '0')} />
      </Group>
      <Group grow>
        <TextInput label="Description" value={description} onChange={(e) => setDescription(e.target.value)} />
        <TextInput label="Engineering Unit" value={engUnit} onChange={(e) => setEngUnit(e.target.value)} />
      </Group>
      <Switch label="Enabled" checked={enabled} onChange={(e) => setEnabled(e.currentTarget.checked)} />

      <Text fw={600} size="sm" mt="xs">Signal Profile</Text>
      <Select
        label="Profile Type"
        data={profileDefinitions.map((p) => ({ value: p.id, label: p.label }))}
        value={profile}
        onChange={(v) => {
          const newProfile = v ?? 'static'
          setProfile(newProfile)
          setProfileParams(defaultSimulation(newProfile) as Record<string, unknown>)
        }}
      />

      {profileDef && (
        <Stack gap="xs" p="xs" style={{ border: '1px solid #333', borderRadius: 4 }}>
          <Text size="sm" c="dimmed">{profileDef.description}</Text>
          {profileParamFields}
        </Stack>
      )}

      <SignalPreviewChart profile={profile} params={profileParams} dataType={dataType} />

      <Group justify="flex-end" mt="sm">
        <Button variant="light" onClick={onClose}>Cancel</Button>
        <Button onClick={handleSave}>{isEdit ? 'Save Changes' : 'Add Tag'}</Button>
      </Group>
    </Stack>
  )
}

function StringArrayEditor({ values, onChange }: { values: string[]; onChange: (v: string[]) => void }) {
  const add = () => onChange([...values, ''])
  const remove = (idx: number) => onChange(values.filter((_, i) => i !== idx))
  const update = (idx: number, val: string) => {
    const next = [...values]
    next[idx] = val
    onChange(next)
  }
  return (
    <Stack gap={4}>
      {values.map((v, i) => (
        <Group key={i} gap={4}>
          <TextInput size="xs" value={v} onChange={(e) => update(i, e.target.value)} style={{ flex: 1 }} />
          <ActionIcon size="sm" color="red" variant="light" onClick={() => remove(i)}>
            <IconTrash size={12} />
          </ActionIcon>
        </Group>
      ))}
      <Button size="compact-xs" variant="light" leftSection={<IconPlus size={12} />} onClick={add}>Add</Button>
    </Stack>
  )
}
