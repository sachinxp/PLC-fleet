import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { SimpleGrid, Card, Group, Text, Badge, Button, Title, Stack, RingProgress, Center, ActionIcon, Tooltip } from '@mantine/core'
import { IconTrash, IconPencil } from '@tabler/icons-react'
import { useFleetStore } from '../stores/fleetStore'
import { useSignalR } from '../hooks/useSignalR'
import PlcBrandImage from '../components/PlcBrandImage'
import ConfirmDialog from '../components/ConfirmDialog'
import { notifySuccess, notifyError } from '../lib/notifications'
import * as plcsApi from '../api/plcs'
import { brandNames, brandColors, stateLabels, stateColors, Brand, PlcState } from '../types'

export default function FleetDashboard() {
  const navigate = useNavigate()
  const plcs = useFleetStore(s => s.plcs)
  const loading = useFleetStore(s => s.loading)
  const load = useFleetStore(s => s.load)
  const removePlc = useFleetStore(s => s.removePlc)
  const [deleteId, setDeleteId] = useState<string | null>(null)
  useSignalR()

  useEffect(() => { load() }, [load])

  const toggleState = async (plc: { id: string; state: number; name: string }) => {
    try {
      if (plc.state === PlcState.Running) {
        await plcsApi.stop(plc.id)
        notifySuccess('PLC Stopped', plc.name)
      } else {
        await plcsApi.start(plc.id)
        notifySuccess('PLC Started', plc.name)
      }
      load()
    } catch {
      notifyError('Failed to toggle state', plc.name)
    }
  }

  const handleDelete = async () => {
    if (!deleteId) return
    try {
      await plcsApi.remove(deleteId)
      removePlc(deleteId)
      notifySuccess('PLC Deleted')
      setDeleteId(null)
    } catch {
      notifyError('Failed to delete PLC')
    }
  }

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}>Fleet Dashboard</Title>
        <Group>
          <Button onClick={load} variant="light" loading={loading}>Refresh</Button>
          <Button onClick={() => navigate('/plcs/new')}>New PLC</Button>
        </Group>
      </Group>

      {plcs.length === 0 && (
        <Card withBorder p="xl">
          <Center>
            <Stack align="center" gap="sm">
              <Text c="dimmed" size="lg">No PLCs configured</Text>
              <Button onClick={() => navigate('/plcs/new')}>Create your first PLC</Button>
            </Stack>
          </Center>
        </Card>
      )}

      <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
        {plcs.map(plc => (
          <Card key={plc.id} withBorder padding="md" radius="md" style={{ cursor: 'pointer' }}>
            <Stack gap="xs">
              <Group justify="space-between" wrap="nowrap" align="flex-start">
                <PlcBrandImage brand={plc.brand as Brand} state={plc.state} />
                <Stack gap={4} align="flex-end">
                  <Badge color={brandColors[plc.brand as Brand]}>{brandNames[plc.brand as Brand]}</Badge>
                  <Badge color={stateColors[plc.state as PlcState]} variant="dot">{stateLabels[plc.state as PlcState]}</Badge>
                </Stack>
              </Group>
              <Text fw={500} size="lg" onClick={() => navigate(`/plcs/${plc.id}`)}>{plc.name}</Text>
              <Text size="sm" c="dimmed">{plc.personality}</Text>
              <Text size="sm">{plc.network.ipAddress}:{plc.network.port}</Text>
              <Group gap="md">
                <RingProgress size={60} thickness={6} sections={[{ value: 100, color: brandColors[plc.brand as Brand] }]} label={<Text ta="center" size="xs">{plc.tags?.length ?? 0}</Text>} />
                <Stack gap={0}>
                  <Text size="xs" c="dimmed">Tags</Text>
                  <Text size="xs" c="dimmed">Conn: {plc.activeConnections}</Text>
                  <Text size="xs" c="dimmed">Req: {plc.requestsServed}</Text>
                </Stack>
              </Group>
              <Group justify="space-between">
                <Group gap="xs">
                  <Button size="xs" variant="light" color={plc.state === PlcState.Running ? 'orange' : 'green'} onClick={() => toggleState({ id: plc.id, state: plc.state, name: plc.name })}>
                    {plc.state === PlcState.Running ? 'Stop' : 'Start'}
                  </Button>
                  <Tooltip label="Edit">
                    <ActionIcon variant="light" color="blue" size="sm" onClick={() => navigate(`/plcs/${plc.id}`)}>
                      <IconPencil size={14} />
                    </ActionIcon>
                  </Tooltip>
                </Group>
                <Tooltip label="Delete">
                  <ActionIcon variant="light" color="red" size="sm" onClick={() => setDeleteId(plc.id)}>
                    <IconTrash size={14} />
                  </ActionIcon>
                </Tooltip>
              </Group>
            </Stack>
          </Card>
        ))}
      </SimpleGrid>

      <ConfirmDialog
        opened={!!deleteId}
        onClose={() => setDeleteId(null)}
        onConfirm={handleDelete}
        title="Delete PLC"
        message="Are you sure you want to delete this PLC? This action cannot be undone."
        confirmLabel="Delete"
      />
    </Stack>
  )
}
