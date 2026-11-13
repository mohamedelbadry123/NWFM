/** Human-facing labels do not change executable node types or diagnostic codes. */
export function workflowElementLabel(type: string, arabic = false): string {
  const names: Record<string, [string, string]> = {
    UserTask: ['Step', 'خطوة'],
    MainActivity: ['Task Type', 'نوع المهمة'],
    ServiceTask: ['Event', 'حدث'],
    NotificationTask: ['Notification', 'إشعار'],
    WaitEvent: ['Wait for Event', 'انتظار حدث'],
    Timer: ['Timer', 'مؤقت'],
    ScriptTask: ['Set values', 'تعيين قيم'],
    CallActivity: ['Child workflow', 'سير عمل فرعي'],
    ExclusiveGateway: ['Condition', 'شرط'],
    ParallelGateway: ['Fork', 'تفرع'],
    InclusiveGateway: ['Fork', 'تفرع'],
    JoinGateway: ['Join / Wait', 'التقاء / انتظار'],
    Start: ['Start', 'بداية'],
    End: ['End', 'نهاية'],
  };
  return (names[type] || [type, type])[arabic ? 1 : 0];
}

export function workflowIssueLabel(
  message: string,
  code?: string | null,
  name?: string,
  arabic = false,
): string {
  if (!name) return message;
  const key = (code || '').toLowerCase();
  if (key === 'no_outgoing_transition')
    return arabic ? `اختر أين يستمر ${name}.` : `Choose where ${name} continues.`;
  if (key === 'no_incoming_transition')
    return arabic ? `صل عنصراً سابقاً بـ ${name}.` : `Connect a preceding element to ${name}.`;
  if (key.includes('assignment') || key.includes('role') || key.includes('group'))
    return arabic ? `اختر من يتلقى ${name}.` : `Choose who receives ${name}.`;
  return message;
}
