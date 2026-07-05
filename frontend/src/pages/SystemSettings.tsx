import { useState, useEffect } from 'react'
import { Stack, Title, Card, Text, Badge, Code } from '@mantine/core'
import * as systemApi from '../api/system'
import type { SystemInfo } from '../api/system'

export default function SystemSettings() {
  const [info, setInfo] = useState<SystemInfo>({ version: '', dotnetVersion: '', os: '' })

  useEffect(() => {
    systemApi.getInfo().then(setInfo).catch(() => {})
  }, [])

  return (
    <Stack>
      <Title order={3}>System</Title>
      <Card withBorder>
        <Text fw={500} mb="md">Elevation Status</Text>
        <Badge color="yellow">Loopback Mode (no elevation needed)</Badge>
        <Text size="sm" mt="sm" c="dimmed">NIC alias management requires administrator privileges. The application runs in loopback mode for development.</Text>
      </Card>
      <Card withBorder>
        <Text fw={500} mb="md">Application Info</Text>
        <Stack gap="xs">
          <Text size="sm"><Code>Version:</Code> {info.version || 'N/A'}</Text>
          <Text size="sm"><Code>Runtime:</Code> {info.dotnetVersion || 'N/A'}</Text>
          <Text size="sm"><Code>OS:</Code> {info.os || 'N/A'}</Text>
        </Stack>
      </Card>
    </Stack>
  )
}
