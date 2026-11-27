#!/usr/bin/env bash
# Checks out the bamtk framework submodules bam.maui builds against: the closure of every sibling a project here
# references, directly or through another framework project.  Run from the bamtk root, with bam.maui at
# submodules/bam.maui.  Extra arguments are passed to `git submodule update` (CI passes --depth 1).
set -euo pipefail

# Prints the framework libraries the given project files reference, in either style:
# $(BamFrameworkRoot)<lib>\... or ..\..\<lib>\...  Backslashes are turned into slashes first.
refs() {
  cat "$@" 2>/dev/null \
    | tr '\\' '/' \
    | grep -oE 'BamFrameworkRoot\)[A-Za-z0-9._-]+|\.\./\.\./[A-Za-z0-9._-]+/' \
    | sed -E -e 's#BamFrameworkRoot\)##' -e 's#^\.\./\.\./##' -e 's#/$##' \
    | sort -u || true
}

todo="$(refs $(find submodules/bam.maui -name '*.csproj') | tr '\n' ' ')"
seen=" bam.maui "
while [ -n "${todo// /}" ]; do
  read -r lib rest <<< "$todo"
  todo="$rest"
  case "$seen" in *" $lib "*) continue;; esac
  # Only registered submodules count: a relative path can also reach a folder such as .nuget.
  git config -f .gitmodules --get-regexp '\.path$' | grep -qx ".* submodules/$lib" || continue
  seen="$seen$lib "
  git submodule update --init -q "$@" -- "submodules/$lib"
  todo="$todo $(refs $(find "submodules/$lib" -name '*.csproj' -not -name '*tests*.csproj') | tr '\n' ' ')"
done
echo "framework submodules:$seen"
