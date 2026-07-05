import { useState, useEffect } from 'react'
import { Stack, Title, Card, Group, Text, Badge, Table } from '@mantine/core'
import * as networkApi from '../api/network'
import type { NetworkStatus } from '../api/network'

export default function NetworkSettings() {
  const [nics, setNics] = useState<string[]>([])
  const [status, setStatus] = useState<NetworkStatus>({ isElevated: false, message: '' })

  useEffect(() => {
    networkApi.getNics().then(setNics).catch(() => {})
    networkApi.getStatus().then(setStatus).catch(() => {})
  }, [])

  return (
    <Stack>
      <Title order={3}>Network Settings</Title>
      <Card withBorder>
        <Text fw={500} mb="md">Status</Text>
        <Group>
          <Badge color={status.isElevated ? 'green' : 'yellow'}>
            {status.isElevated ? 'Elevated' : 'Loopback Mode'}
          </Badge>
          <Text size="sm" c="dimmed">{status.message}</Text>
        </Group>
      </Card>
      <Card withBorder>
        <Text fw={500} mb="md">Available Network Interfaces</Text>
        {nics.length === 0 ? (
          <Text c="dimmed" size="sm">No interfaces found or running in loopback mode</Text>
        ) : (
          <Table>
            <Table.Thead><Table.Tr><Table.Th>Name</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>{nics.map(n => <Table.Tr key={n}><Table.Td>{n}</Table.Td></Table.Tr>)}</Table.Tbody>
          </Table>
        )}
      </Card>
    </Stack>
  )
}
