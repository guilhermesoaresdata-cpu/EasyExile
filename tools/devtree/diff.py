"""Compare two dev-tree dumps and report what changed.

  python diff.py antes.jsonl depois.jsonl [mais.jsonl ...]

The reverse-engineering loop, borrowed from DevTreeMcp: capture, do a known
thing in the game, capture again, and let the difference name the subtree that
appeared. Hunting for it by eye in fifty thousand lines is how an evening goes.

Give three or more files and anything that differs between the FIRST TWO is
treated as volatile - per-frame churn that changes on its own - and hidden, so a
real one-off change is not buried under a clock and an animation.
"""

import collections
import json
import sys


def load(path):
    """Nodes by their path from the root, which is the only stable identity."""
    nodes = {}

    with open(path, encoding="utf-8") as handle:
        for line in handle:
            try:
                node = json.loads(line)
            except json.JSONDecodeError:
                continue

            if node.get("kind") == "context":
                continue

            key = tuple(node.get("path") or [])

            if key:
                nodes[key] = node

    return nodes


def describe(node):
    text = node.get("t538") or node.get("t360") or node.get("t710") or ""
    r = node.get("r") or [0, 0, 0, 0]

    return f"{r[2]:.0f}x{r[3]:.0f} @({r[0]:.0f},{r[1]:.0f}) {'vis' if node.get('vis') else '---'}  {text[:60]}"


def main():
    if len(sys.argv) < 3:
        print(__doc__)

        return 1

    before = load(sys.argv[1])
    after = load(sys.argv[2])

    # Anything already unstable between two "before" samples is noise.
    volatile = set()

    for extra in sys.argv[3:]:
        other = load(extra)

        for key, node in before.items():
            if key in other and json.dumps(other[key], sort_keys=True) != json.dumps(node, sort_keys=True):
                volatile.add(key)

    appeared = [k for k in after if k not in before and k not in volatile]
    vanished = [k for k in before if k not in after and k not in volatile]

    moved = [
        k for k in after
        if k in before and k not in volatile and after[k].get("r") != before[k].get("r")
    ]

    print(f"apareceram: {len(appeared)}   sumiram: {len(vanished)}   moveram: {len(moved)}")

    if volatile:
        print(f"({len(volatile)} caminhos mudam sozinhos e foram ignorados)")

    for title, keys, source in (
        ("APARECERAM", appeared, after),
        ("SUMIRAM", vanished, before),
        ("MOVERAM", moved, after),
    ):
        if not keys:
            continue

        print(f"\n== {title}")

        # Shallowest first: the outermost thing that appeared is the thing that
        # appeared, and everything under it came along for the ride.
        for key in sorted(keys, key=len)[:40]:
            print(f"  {'.'.join(map(str, key))}  {describe(source[key])}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
