// The top-edge tuck-shape regression assertions, kept out of the settings page script so that
// page stays inside the file-size limit. The shape has three choices, defaults to a thin line,
// and the picked value is saved to the island settings.
type Check = (condition: unknown, message: string) => void;

export async function checkTuckShape(
  check: Check,
  generalTitles: (string | null)[],
  act: (callback: () => void | Promise<void>) => Promise<void>,
): Promise<void> {
  check(generalTitles.includes("贴边形态"), "General offers the top-edge tuck shape");
  const shapeGroup = [...document.querySelectorAll<HTMLElement>("[role='group']")]
    .find((group) => group.getAttribute("aria-label") === "贴边形态");
  const shapeOptions = () => [...(shapeGroup?.querySelectorAll<HTMLButtonElement>("button") ?? [])];
  const storedShape = () =>
    (JSON.parse(localStorage.getItem("focusd-island-settings") ?? "{}") as { tuckShape?: string }).tuckShape;

  check(shapeOptions().map((button) => button.textContent)?.join(",") === "细线,隐藏,露边"
    && shapeOptions()[0]?.getAttribute("aria-pressed") === "true"
    && storedShape() === "thread",
    "Tuck shape offers thread, hidden and sliver, and defaults to a thin line");
  await act(async () => { shapeOptions()[1].click(); });
  check(storedShape() === "hidden"
    && shapeOptions()[1].getAttribute("aria-pressed") === "true"
    && shapeOptions()[0].getAttribute("aria-pressed") === "false",
    "Choosing hidden saves it and moves the pressed option");
  await act(async () => { shapeOptions()[0].click(); });
}
