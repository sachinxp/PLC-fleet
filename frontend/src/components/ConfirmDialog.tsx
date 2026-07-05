import { Modal, Text, Group, Button } from '@mantine/core'

interface ConfirmDialogProps {
  opened: boolean
  onClose: () => void
  onConfirm: () => void
  title: string
  message: string
  confirmLabel?: string
  color?: string
}

export default function ConfirmDialog({ opened, onClose, onConfirm, title, message, confirmLabel = 'Confirm', color = 'red' }: ConfirmDialogProps) {
  return (
    <Modal opened={opened} onClose={onClose} title={title} size="sm">
      <Text mb="lg">{message}</Text>
      <Group justify="flex-end">
        <Button variant="light" onClick={onClose}>Cancel</Button>
        <Button color={color} onClick={() => { onConfirm(); onClose() }}>{confirmLabel}</Button>
      </Group>
    </Modal>
  )
}
