"""Build the skill -> recommended supports table the overlay reads.

Run once. It fetches the skill index from poe2db, then one page per skill, and
writes supports.json next to itself.

  python fetch.py [--out supports.json]

Two things it is careful about, both deliberate:

It is polite. One pass, half a second between requests, and every page cached on
disk - so a re-run after a parse fix costs nothing and a third-party site is not
hit 192 times to fix a regex.

It is honest about what the data is. poe2db publishes RECOMMENDED supports in
five tiers, curated, not "most used" statistics gathered from real builds. Those
would come from build aggregators and are a different thing. The tier order is
preserved on the way out so the first entries really are the most recommended,
which is what the overlay shows.
"""

import argparse
import html
import json
import os
import re
import sys
import time
import urllib.request

BASE = "https://poe2db.tw/us/"
INDEX = BASE + "Skill_Gems"
AGENT = "EasyExile/1.0 (overlay support-advice table; one-off build)"

HERE = os.path.dirname(os.path.abspath(__file__))
CACHE = os.path.join(HERE, "cache")

# The anchors poe2db puts on gem links. BOTH classes matter: the coloured ones
# are the older gems and "gemitem" is what the newer pages use, and taking only
# the coloured set silently lost 215 skills - Volcano among them, which is how it
# was noticed. Neither class is used for support gems on the index, so this stays
# a list of actives.
GEM_LINK = re.compile(
    r'<a class="(?:gem_[a-z]+|gemitem)"[^>]*href="/us/([^"]+)"[^>]*>([^<]+)</a>')

# The block this whole script exists for. The heading is followed by a table
# whose rows are tiers and whose cells are the gems of that tier.
BLOCK = re.compile(
    r"Recommended Support Gems.*?<tbody[^>]*>(.*?)</tbody>", re.S)

ROW = re.compile(r"<tr>(.*?)</tr>", re.S)


def get(url, cache_key, delay=0.5):
    """Fetch once, then from disk forever."""
    os.makedirs(CACHE, exist_ok=True)

    path = os.path.join(CACHE, cache_key + ".html")

    if os.path.exists(path) and os.path.getsize(path) > 2000:
        with open(path, encoding="utf-8", errors="ignore") as handle:
            return handle.read()

    request = urllib.request.Request(url, headers={"User-Agent": AGENT})

    with urllib.request.urlopen(request, timeout=30) as response:
        body = response.read().decode("utf-8", "ignore")

    with open(path, "w", encoding="utf-8") as handle:
        handle.write(body)

    time.sleep(delay)

    return body


def skills(index):
    """Every active skill on the index, in the order it lists them."""
    found = {}

    for slug, name in GEM_LINK.findall(index):
        found.setdefault(slug, html.unescape(name).strip())

    return found


def supports(page):
    """The recommended supports, tier by tier, flattened in tier order."""
    block = BLOCK.search(page)

    if not block:
        return []

    names = []

    for row in ROW.findall(block.group(1)):
        for _, name in GEM_LINK.findall(row):
            name = html.unescape(name).strip()

            # A gem recommended at two tiers is one gem. Keeping the first
            # mention keeps it at its best tier, which is the ranking.
            if name and name not in names:
                names.append(name)

    return names


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default=os.path.join(HERE, "supports.json"))
    parser.add_argument("--delay", type=float, default=0.5)

    args = parser.parse_args()

    print("indice...", flush=True)

    found = skills(get(INDEX, "_index", args.delay))

    print(f"{len(found)} skills", flush=True)

    table = {}
    missing = []

    for i, (slug, name) in enumerate(sorted(found.items(), key=lambda kv: kv[1]), 1):
        try:
            advice = supports(get(BASE + slug, slug.replace("/", "_"), args.delay))
        except Exception as error:                      # noqa: BLE001
            print(f"  {i:3}/{len(found)} {name}: FALHOU {error}", flush=True)
            missing.append(name)
            continue

        if advice:
            table[name] = advice
        else:
            missing.append(name)

        if i % 20 == 0:
            print(f"  {i}/{len(found)}", flush=True)

    with open(args.out, "w", encoding="utf-8") as handle:
        json.dump(table, handle, ensure_ascii=False, indent=1, sort_keys=True)

    print(f"\n{len(table)} skills com recomendacao -> {args.out}")
    print(f"{len(missing)} sem bloco de recomendacao")

    if missing:
        print("  " + ", ".join(missing[:15]) + ("..." if len(missing) > 15 else ""))

    return 0


if __name__ == "__main__":
    sys.exit(main())
