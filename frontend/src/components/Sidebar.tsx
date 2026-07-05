import { useState, useEffect } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { Stack, Group, NavLink as MantineNavLink, Badge, Text, Divider } from '@mantine/core'
import { IconDeviceSdCard, IconNetwork, IconFileExport, IconSettings, IconPlus } from '@tabler/icons-react'

interface PlcSummary {
  id: string
  name: string
  brand: number
  state: number
}

const brandNames = ['Siemens', 'Rockwell', 'Modbus', 'Mitsubishi', 'Beckhoff', 'OPC UA']
const stateLabels = ['Created', 'Running', 'Stopped', 'Error']
const stateColors = ['gray', 'green', 'orange', 'red']

export default function Sidebar() {
  const location = useLocation()
  const [plcs, setPlcs] = useState<PlcSummary[]>([])

  useEffect(() => {
    fetch('/api/plcs')
      .then(r => r.json())
      .then(setPlcs)
      .catch(() => {})
  }, [location.pathname])

  return (
    <Stack gap="xs">
      <MantineNavLink
        component={NavLink}
        to="/"
        label="Fleet Dashboard"
        leftSection={<IconDeviceSdCard size={20} />}
        active={location.pathname === '/'}
        variant="light"
      />
      <MantineNavLink
        component={NavLink}
        to="/plcs/new"
        label="New PLC"
        leftSection={<IconPlus size={20} />}
        active={location.pathname === '/plcs/new'}
        variant="light"
      />

      <Divider my="xs" />

      {plcs.map(plc => (
        <MantineNavLink
          key={plc.id}
          component={NavLink}
          to={`/plcs/${plc.id}`}
          label={
            <Group gap="xs" justify="space-between" style={{ width: '100%' }}>
              <Text size="sm">{plc.name}</Text>
              <Badge size="sm" color={stateColors[plc.state]} variant="dot">
                {stateLabels[plc.state]}
              </Badge>
            </Group>
          }
          description={brandNames[plc.brand] ?? 'Unknown'}
          active={location.pathname === `/plcs/${plc.id}`}
          variant="light"
          style={{ borderRadius: 8 }}
        />
      ))}

      <Divider my="xs" />

      <MantineNavLink
        component={NavLink}
        to="/network"
        label="Network"
        leftSection={<IconNetwork size={20} />}
        active={location.pathname === '/network'}
        variant="light"
      />
      <MantineNavLink
        component={NavLink}
        to="/export"
        label="Export/Import"
        leftSection={<IconFileExport size={20} />}
        active={location.pathname === '/export'}
        variant="light"
      />
      <MantineNavLink
        component={NavLink}
        to="/system"
        label="System"
        leftSection={<IconSettings size={20} />}
        active={location.pathname === '/system'}
        variant="light"
      />
    </Stack>
  )
}
