/** Two concentric rings in the current `--quota-color`, drawn on a 24-unit grid. */
export function QuotaRings({ outer, inner, size = 22 }: { outer: number | null; inner: number | null; size?: number }) {
  return (
    <svg className="quota-rings" width={size} height={size} viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <Ring radius={10} left={outer} unknown={outer === null} />
      <Ring radius={5.5} left={inner} unknown={inner === null} />
    </svg>
  );
}

function Ring({ radius, left, unknown }: { radius: number; left: number | null; unknown: boolean }) {
  // (E1) Unknown usage is a full pale ring, not a sliver or a bare track. A sliver still
  // shows that something is left; a full ring is drawn whole.
  const shown = unknown ? 100 : left !== null && left > 0 ? Math.min(100, Math.max(left, 3)) : 0;
  return (
    <>
      <circle className="quota-rings__track" cx={12} cy={12} r={radius} />
      {shown > 0 && (
        <circle
          className={unknown ? "quota-rings__fill quota-rings__fill--unknown" : "quota-rings__fill"}
          cx={12}
          cy={12}
          r={radius}
          pathLength={100}
          strokeDasharray={shown >= 100 ? undefined : `${shown} 100`}
          transform="rotate(-90 12 12)"
        />
      )}
    </>
  );
}
