import { Routes, Route } from 'react-router-dom'
import { AppShell, Group, Title, ThemeIcon } from '@mantine/core'
import { Notifications } from '@mantine/notifications'
import { IconDeviceSdCard } from '@tabler/icons-react'
import FleetDashboard from './pages/FleetDashboard'
import PlcDetail from './pages/PlcDetail'
import NewPlcWizard from './pages/NewPlcWizard'
import NetworkSettings from './pages/NetworkSettings'
import ExportImport from './pages/ExportImport'
import SystemSettings from './pages/SystemSettings'
import Sidebar from './components/Sidebar'

export default function App() {
  return (
    <>
      <Notifications position="top-right" />
      <AppShell
        navbar={{ width: 250, breakpoint: 'sm' }}
        header={{ height: 56 }}
        padding="md"
      >
        <AppShell.Header>
          <Group h="100%" px="md">
            <ThemeIcon size="lg" variant="gradient" gradient={{ from: 'blue', to: 'cyan' }}>
              <IconDeviceSdCard size={20} />
            </ThemeIcon>
            <Title order={4}>PLC Simulator</Title>
          </Group>
        </AppShell.Header>

        <AppShell.Navbar p="xs">
          <Sidebar />
        </AppShell.Navbar>

        <AppShell.Main>
          <Routes>
            <Route path="/" element={<FleetDashboard />} />
            <Route path="/plcs/:id" element={<PlcDetail />} />
            <Route path="/plcs/new" element={<NewPlcWizard />} />
            <Route path="/network" element={<NetworkSettings />} />
            <Route path="/export" element={<ExportImport />} />
            <Route path="/system" element={<SystemSettings />} />
          </Routes>
        </AppShell.Main>
      </AppShell>
    </>
  )
}
