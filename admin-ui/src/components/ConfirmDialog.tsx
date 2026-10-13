import type { ReactNode } from 'react'
import { ErrorNote } from '@/components/ErrorNote'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'

type Props = {
  open: boolean
  title: string
  description: ReactNode
  confirmLabel: string
  destructive?: boolean
  pending: boolean
  error: unknown
  /** Unset = nothing left to confirm: the dialog only informs and offers Close. */
  onConfirm?: () => void
  onCancel: () => void
}

export function ConfirmDialog({ open, title, description, confirmLabel, destructive, pending, error, onConfirm, onCancel }: Props) {
  return (
    <Dialog open={open} onOpenChange={(next) => { if (!next) onCancel() }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        {error ? <ErrorNote error={error} /> : null}
        <DialogFooter>
          <Button variant="outline" onClick={onCancel}>{onConfirm ? 'Cancel' : 'Close'}</Button>
          {onConfirm ? <Button variant={destructive ? 'destructive' : 'default'} disabled={pending} onClick={onConfirm}>{confirmLabel}</Button> : null}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
