import { useEffect, useState } from "react";
import { currentTodoFileStatus, subscribeTodoFileStatus, type TodoFileStatus } from "./fileSync";

/** The todo file's state, kept in sync with the mirror rather than polled. */
export function useTodoFileStatus() {
  const [status, setStatus] = useState<TodoFileStatus | null>(currentTodoFileStatus);

  useEffect(() => subscribeTodoFileStatus(setStatus), []);

  return status;
}
