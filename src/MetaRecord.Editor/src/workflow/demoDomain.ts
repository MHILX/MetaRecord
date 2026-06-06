export const demoDomain = {
  preferredWorkflowName: 'Capture todo snapshot',
  captureAuditSnapshotWorkflowName: 'Capture todo snapshot',
  rejectInvalidTodoTitleWorkflowName: 'Reject empty todo title',
  createdLogWorkflowName: 'Write log when todo is created',
  completedLogWorkflowName: 'Write log when todo is completed'
} as const;
