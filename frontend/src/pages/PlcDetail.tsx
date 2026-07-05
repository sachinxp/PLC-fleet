import { useState, useEffect } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { Stack, Group, Title, Badge, Button, Card, Table, Text, ActionIcon, Switch, Tooltip, Modal, Center } from '@mantine/core'
import { IconTrash, IconPlus, IconPencil, IconPlayerPlay, IconPlayerStop, IconArrowBack } from '@tabler/icons-react'
import type { PlcInstance, TagDefinition, Brand } from '../types'
import { brandNames, brandColors, stateLabels, stateColors, TagAccess, PlcState } from '../types'
import TagEditorModal from '../components/TagEditorModal'
import PlcBrandImage from '../components/PlcBrandImage'
import ConfirmDialog from '../components/ConfirmDialog'
import { notifySuccess, notifyError } from '../lib/notifications'
import * as plcsApi from '../api/plcs'
import * as tagsApi from '../api/tags'

export default function PlcDetail() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [plc, setPlc] = useState<PlcInstance | null>(null)
  const [notFound, setNotFound] = useState(false)
  const [editorOpen, setEditorOpen] = useState(false)
  const [editingTag, setEditingTag] = useState<TagDefinition | null>(null)
  const [deleteTagName, setDeleteTagName] = useState<string | null>(null)
  const [deletePlcOpen, setDeletePlcOpen] = useState(false)

  const load = () => {
    if (!id) return
    setNotFound(false)
    plcsApi.getById(id).then(setPlc).catch((err) => {
      if (err?.status === 404) setNotFound(true)
      else navigate('/')
    })
  }

  useEffect(load, [id])

  const deleteTag = async () => {
    if (!id || !deleteTagName) return
    await tagsApi.remove(id, deleteTagName)
    setDeleteTagName(null)
    notifySuccess('Tag Deleted', deleteTagName)
    load()
  }

  const deletePlc = async () => {
    if (!id) return
    await plcsApi.remove(id)
    notifySuccess('PLC Deleted', plc?.name)
    navigate('/')
  }

  const toggleState = async () => {
    if (!id || !plc) return
    try {
      if (plc.state === PlcState.Running) {
        await plcsApi.stop(id)
        notifySuccess('PLC Stopped', plc.name)
      } else {
        await plcsApi.start(id)
        notifySuccess('PLC Started', plc.name)
      }
      load()
    } catch {
      notifyError('Failed to toggle state', plc.name)
    }
  }

  if (notFound) return <Center><Text c="dimmed" size="lg">PLC not found</Text></Center>
  if (!plc) return <Center><Text c="dimmed">Loading...</Text></Center>

  return (
    <Stack>
      <Group justify="space-between">
        <Group>
          <PlcBrandImage brand={plc.brand as Brand} state={plc.state} height={100} />
          <Stack gap={4}>
            <Title order={3}>{plc.name}</Title>
            <Group>
              <Badge color={brandColors[plc.brand]}>{brandNames[plc.brand]}</Badge>
              <Badge color={stateColors[plc.state]} variant="dot">{stateLabels[plc.state]}</Badge>
              <Text size="sm" c="dimmed">{plc.personality}</Text>
            </Group>
          </Stack>
        </Group>
        <Group>
          <Text size="sm" ff="monospace">{plc.network.ipAddress}:{plc.network.port}</Text>
          <Button variant="light" onClick={() => navigate('/')} leftSection={<IconArrowBack size={14} />}>Back</Button>
        </Group>
      </Group>

      <Group>
        <Button
          color={plc.state === PlcState.Running ? 'orange' : 'green'}
          leftSection={plc.state === PlcState.Running ? <IconPlayerStop size={14} /> : <IconPlayerPlay size={14} />}
          onClick={toggleState}
        >
          {plc.state === PlcState.Running ? 'Stop' : 'Start'}
        </Button>
        <Button color="red" variant="light" onClick={() => setDeletePlcOpen(true)}>Delete PLC</Button>
      </Group>

      <Card withBorder>
        <Group justify="space-between" mb="md">
          <Text fw={500}>Tags ({plc.tags.length})</Text>
          <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => { setEditingTag(null); setEditorOpen(true) }}>Add Tag</Button>
        </Group>

        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Name</Table.Th>
              <Table.Th>Address</Table.Th>
              <Table.Th>Type</Table.Th>
              <Table.Th>Access</Table.Th>
              <Table.Th>Profile</Table.Th>
              <Table.Th>Enabled</Table.Th>
              <Table.Th w={100}>Actions</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {plc.tags.length === 0 ? (
              <Table.Tr>
                <Table.Td colSpan={7}>
                  <Text ta="center" c="dimmed" py="lg">No tags. Click "Add Tag" to create one.</Text>
                </Table.Td>
              </Table.Tr>
            ) : (
              plc.tags.map(tag => (
                <Table.Tr key={tag.name}>
                  <Table.Td><Text size="sm" fw={500}>{tag.name}</Text></Table.Td>
                  <Table.Td><Text size="sm" ff="monospace">{tag.address}</Text></Table.Td>
                  <Table.Td><Badge size="sm" variant="light">{tag.dataType}</Badge></Table.Td>
                  <Table.Td><Text size="sm">{tag.access === TagAccess.ReadOnly ? 'RO' : 'RW'}</Text></Table.Td>
                  <Table.Td><Badge size="sm" color="gray">{tag.simulation.profile}</Badge></Table.Td>
                  <Table.Td><Switch checked={tag.enabled} readOnly size="xs" /></Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      <Tooltip label="Edit">
                        <ActionIcon variant="light" color="blue" size="sm" onClick={() => { setEditingTag(tag); setEditorOpen(true) }}>
                          <IconPencil size={14} />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label="Delete">
                        <ActionIcon variant="light" color="red" size="sm" onClick={() => setDeleteTagName(tag.name)}>
                          <IconTrash size={14} />
                        </ActionIcon>
                      </Tooltip>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))
            )}
          </Table.Tbody>
        </Table>
      </Card>

      <Modal opened={editorOpen} onClose={() => setEditorOpen(false)} title={editingTag ? 'Edit Tag' : 'Add Tag'} size="lg">
        <TagEditorModal
          opened={editorOpen}
          onClose={() => setEditorOpen(false)}
          onSave={async (tag) => {
            if (!id) return
            if (editingTag) {
              await tagsApi.update(id, editingTag.name, tag)
            } else {
              await tagsApi.create(id, tag)
            }
            load()
          }}
          plcId={id ?? ''}
          initialTag={editingTag}
        />
      </Modal>

      <ConfirmDialog
        opened={!!deleteTagName}
        onClose={() => setDeleteTagName(null)}
        onConfirm={deleteTag}
        title="Delete Tag"
        message={`Are you sure you want to delete tag "${deleteTagName}"?`}
        confirmLabel="Delete"
      />

      <ConfirmDialog
        opened={deletePlcOpen}
        onClose={() => setDeletePlcOpen(false)}
        onConfirm={deletePlc}
        title="Delete PLC"
        message={`Are you sure you want to delete PLC "${plc.name}"? All tags and configuration will be lost.`}
        confirmLabel="Delete PLC"
      />
    </Stack>
  )
}
