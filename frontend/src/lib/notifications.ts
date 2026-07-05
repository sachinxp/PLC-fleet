import { notifications } from '@mantine/notifications'

export function notifySuccess(title: string, message?: string) {
  notifications.show({ color: 'green', title, message, autoClose: 3000 })
}

export function notifyError(title: string, message?: string) {
  notifications.show({ color: 'red', title, message, autoClose: 5000 })
}

export function notifyWarning(title: string, message?: string) {
  notifications.show({ color: 'orange', title, message, autoClose: 4000 })
}
