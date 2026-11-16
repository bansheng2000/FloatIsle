import { AgentHooksSettingsSection } from "../../features/agent-status/AgentHooksSettingsSection";
import { AgentReplyMarkerSettings } from "../../features/agent-status/AgentReplyMarkerSettings";
import { DshBridgeSection } from "../../features/agent-status/DshBridgeSection";
import { DshUsageSection } from "../../features/agent-status/DshUsageSection";
import { useAgentStatusSettings } from "../../features/agent-status/useAgentStatusSettings";
import type { IslandSettings } from "../../features/appearance/types";

/**
 * Settings → AI Agent → Connection: installing and repairing the Claude Code and Codex hooks, each
 * agent's state, right below them the reply status marker, and last what the DSH notifier bridge
 * reports about itself, with the DSH source switch beside it, and finally the three usage
 * numbers, each from its own source.
 */
export function AgentConnectSettingsPage({ settings, onSettingsChange }: {
  settings: IslandSettings;
  onSettingsChange: (settings: IslandSettings) => void;
}) {
  // Owned by the page so agent status is only polled while it is open.
  const agentStatus = useAgentStatusSettings();
  return (
    <>
      <AgentHooksSettingsSection {...agentStatus} />
      <AgentReplyMarkerSettings />
      <DshBridgeSection
        agentShowDsh={settings.agentShowDsh}
        onAgentShowDshChange={(agentShowDsh) => onSettingsChange({ ...settings, agentShowDsh })}
      />
      <DshUsageSection />
    </>
  );
}
