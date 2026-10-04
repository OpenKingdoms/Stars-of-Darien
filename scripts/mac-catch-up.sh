#!/usr/bin/env bash
# Catches this Mac's Stars of Darien checkout up with GitHub. It
# fast-forwards the checkout when no tracked file has changes, then puts the
# engine library engine/VERSION names into unity/Assets/Plugins/macOS the
# way the editor's EngineInstaller does. The library comes from the commit,
# or, while GitHub's Mac build of a new engine waits to land on main, from
# the mac-engine branch or the build's artifact. A checkout with changes is
# left alone. Everything it does goes to the log as well.
#   bash scripts/mac-catch-up.sh                catch up now
#   bash scripts/mac-catch-up.sh install-auto   also catch up at login, on waking and when a network comes up
#   bash scripts/mac-catch-up.sh remove-auto    stop doing that
# Log: ~/Library/Logs/stars-of-darien-catch-up.log
# Environment:
#   SOD_DIR   the checkout, the one this script is in by default
#   SOD_REPO  the GitHub repository, OpenKingdoms/Stars-of-Darien
#   SOD_GH    the gh command that fetches a build artifact, gh
set -uo pipefail

self="$(cd "$(dirname "$0")" && pwd)/$(basename "$0")"
repo="${SOD_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"
slug="${SOD_REPO:-OpenKingdoms/Stars-of-Darien}"
gh="${SOD_GH:-gh}"
launchctl="${SOD_LAUNCHCTL:-launchctl}"
label=net.openkingdoms.stars-of-darien.catch-up
log="$HOME/Library/Logs/stars-of-darien-catch-up.log"
agent="$HOME/Library/LaunchAgents/$label.plist"

# The whole script is parsed before it runs, so a pull that changes this
# file can't change what runs.
main() {
    local mode="${1:-now}"
    case "$mode" in
        now|auto) ;;
        install-auto) install_auto; return ;;
        remove-auto) remove_auto; return ;;
        -h|--help|help) awk 'NR > 1 && !/^#/ { exit } NR > 1 { sub(/^# ?/, ""); print }' "$self"; return 0 ;;
        *) echo "mac-catch-up.sh: unknown mode $mode. Try --help."; return 2 ;;
    esac
    mkdir -p "$(dirname "$log")"
    if [ -f "$log" ] && [ "$(wc -c < "$log")" -gt 1000000 ]; then
        tail -n 3000 "$log" > "$log.tmp" && mv "$log.tmp" "$log"
    fi
    local lock="${TMPDIR:-/tmp}/stars-of-darien-catch-up.lock"
    if ! mkdir "$lock" 2> /dev/null; then
        if kill -0 "$(cat "$lock/pid" 2> /dev/null)" 2> /dev/null; then
            echo "$(stamp) another catch-up is running" >> "$log"
            return 0
        fi
        rm -rf "$lock" && mkdir "$lock" || return 1
    fi
    echo $$ > "$lock/pid"
    tmp=$(mktemp -d "${TMPDIR:-/tmp}/sod-engine.XXXXXX")
    trap 'rm -rf "$lock" "$tmp"' EXIT
    local status
    if [ "$mode" = auto ]; then
        catch_up auto >> "$log" 2>&1
        status=$?
    else
        catch_up now 2>&1 | tee -a "$log"
        status=${PIPESTATUS[0]}
    fi
    return "$status"
}

stamp() { date '+%Y-%m-%d %H:%M:%S'; }

catch_up() {
    local mode="$1" did=""
    echo "== $(stamp) catching up $repo"
    cd "$repo" 2> /dev/null && git rev-parse --is-inside-work-tree > /dev/null 2>&1 \
        || { echo "$repo is not a git checkout. Set SOD_DIR to the Stars of Darien checkout."; return 1; }
    find_python || return 1

    local changes busy=""
    changes=$(git status --porcelain --untracked-files=no)
    for f in MERGE_HEAD CHERRY_PICK_HEAD REVERT_HEAD rebase-merge rebase-apply; do
        [ -e "$(git rev-parse --git-path "$f")" ] && busy="a merge, rebase or cherry-pick is under way"
    done
    if [ -n "$changes$busy" ]; then
        echo "Left alone: ${busy:-tracked files have changes}, so nothing was pulled or installed."
        [ -z "$changes" ] || echo "$changes" | head -10 | sed 's/^/   /'
        return 0
    fi

    local branch remote upstream
    branch=$(git symbolic-ref --short -q HEAD || true)
    remote=$(git config "branch.$branch.remote" 2> /dev/null || echo origin)
    upstream=$(git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2> /dev/null || true)
    local tries=1
    [ "$mode" != auto ] || tries=4
    until fetch "$remote"; do
        tries=$((tries - 1))
        if [ "$tries" -le 0 ]; then
            echo "GitHub can't be reached, so nothing changed. It tries again when the network changes or on the hour."
            return 0
        fi
        sleep 15
    done

    if [ -z "$upstream" ]; then
        echo "${branch:-HEAD} tracks no branch on GitHub, so nothing was pulled."
    else
        local ahead behind old
        read -r ahead behind < <(git rev-list --left-right --count "HEAD...@{u}")
        old=$(git rev-parse --short HEAD)
        if [ "$behind" = 0 ]; then
            echo "$branch is up to date with $upstream at $old."
        elif [ "$ahead" != 0 ]; then
            echo "$branch and $upstream have diverged ($ahead and $behind commits), so nothing was pulled."
        elif git merge --ff-only --quiet "@{u}"; then
            echo "Pulled $behind commits into $branch, $old to $(git rev-parse --short HEAD):"
            git log --format='   %h %s' "$old..HEAD" | head -15
            [ "$behind" -le 15 ] || echo "   and $((behind - 15)) more"
            did="pulled $behind commits"
        else
            echo "The fast-forward of $branch failed, so it stays at $old."
        fi
    fi

    if install_engine "$remote" && [ -n "$installed" ]; then did="${did:+$did, }installed the engine from unity-embed $want"; fi
    if [ -n "$did" ]; then
        echo "Done: $did."
        [ "$mode" != auto ] || notify "Caught up: $did."
    else
        echo "Done: nothing needed doing."
    fi
}

fetch() {
    GIT_TERMINAL_PROMPT=0 GIT_SSH_COMMAND="${GIT_SSH_COMMAND:-ssh -o ConnectTimeout=20 -o BatchMode=yes}" \
        git -c http.lowSpeedLimit=1000 -c http.lowSpeedTime=30 fetch --quiet "$@"
}

# The unity-embed commit a VERSION line names, for the file in $1.
embed_of() { sed -n "s/^$1 .*unity-embed \([0-9a-f]\{7,40\}\).*/\1/p" | head -1; }
same() { [ -n "$1" ] && [ -n "$2" ] && { [ "${2#"$1"}" != "$2" ] || [ "${1#"$2"}" != "$1" ]; }; }

installed=""
want=""
tmp=""
install_engine() {
    local remote="$1" api have committed lib="" from="" ours=0
    api=$(grep -o 'ApiVersion = [0-9]*' unity/Assets/Engine/OkEngine.cs 2> /dev/null | grep -o '[0-9]*$')
    [ -n "$api" ] || { echo "No OkEngine.ApiVersion in unity/Assets/Engine/OkEngine.cs, so the engine was left alone."; return 1; }
    committed="engine/okengine-api$api.dylib"
    want=$(embed_of "okengine-api$api\.dll" < engine/VERSION)
    have=$(embed_of "okengine-api$api\.dylib" < engine/VERSION)
    [ -n "$want" ] || want="$have"
    if [ -z "$want" ]; then
        echo "engine/VERSION names no engine for API $api, so the engine was left alone."
        return 1
    fi

    if [ -f "$committed" ] && same "$have" "$want"; then
        lib="$committed"; from="$committed"; ours=1
    elif fetch "$remote" "+refs/heads/mac-engine:refs/remotes/$remote/mac-engine" \
        && same "$(git cat-file blob "$remote/mac-engine:engine/VERSION" 2> /dev/null | embed_of "okengine-api$api\.dylib")" "$want" \
        && git cat-file blob "$remote/mac-engine:$committed" > "$tmp/libokengine.dylib" 2> /dev/null; then
        lib="$tmp/libokengine.dylib"; from="the mac-engine branch, GitHub's build waiting to land on main"
    elif artifact "$api"; then
        lib="$tmp/libokengine.dylib"; from="GitHub's build artifact $artifact_name"
    elif [ -f "$committed" ]; then
        lib="$committed"; from="$committed"; ours=1
        echo "No Mac build of unity-embed $want was found, so the committed one from unity-embed $have stays in use. It may lack functions the binding calls."
        echo "Run the Mac engine workflow on GitHub (gh workflow run mac-engine.yml -R $slug), or build one here with scripts/build-engine-mac.sh."
        want="$have"
    else
        echo "No Mac build of okengine API $api was found. Run the Mac engine workflow on GitHub (gh workflow run mac-engine.yml -R $slug), or build one here with scripts/build-engine-mac.sh."
        return 1
    fi

    local dir=unity/Assets/Plugins/macOS keep=unity/Library/OkEngine
    local plugin="$dir/libokengine.dylib" record="$keep/installed.json"
    if [ -f "$plugin" ] && cmp -s "$lib" "$plugin"; then
        echo "The engine in Plugins/macOS is already unity-embed $want, from $from."
    else
        if [ -f "$plugin" ] && [ "$(py record-get "$record" libokengine.dylib)" != "$(py hash "$plugin")" ]; then
            mkdir -p "$keep"
            local kept="$keep/replaced-$(date +%Y%m%d-%H%M%S)-libokengine.dylib"
            cp "$plugin" "$kept" && echo "Kept the library that was there, which the editor did not install, as unity/$kept."
        fi
        mkdir -p "$dir"
        # macOS kills a process whose loaded library changes in place, so
        # the library is always a new file.
        rm -f "$plugin"
        cp "$lib" "$plugin" || { echo "Could not copy the engine into $dir."; return 1; }
        installed=1
        echo "Installed the engine from unity-embed $want into Plugins/macOS, from $from."
        if command -v pgrep > /dev/null 2>&1 && pgrep -x Unity > /dev/null 2>&1; then
            echo "Unity is open, so it keeps the engine it loaded until it restarts."
        fi
    fi
    xattr -d com.apple.quarantine "$plugin" 2> /dev/null || true
    local installer=unity/Assets/Game/Editor/EngineInstaller.cs
    if [ -f "$installer" ] && [ "$(py meta "$installer" "$plugin.meta")" = wrote ]; then
        echo "Wrote Unity's import settings for libokengine.dylib."
    fi
    # The editor keeps refreshing a library its record says it installed,
    # so a build that isn't committed yet stays out of the record.
    if [ "$ours" = 1 ]; then
        py record-set "$record" libokengine.dylib "$(py hash "$plugin")"
    else
        py record-drop "$record" libokengine.dylib
    fi
    return 0
}

artifact_name=""
artifact() {
    command -v "$gh" > /dev/null 2>&1 || return 1
    local full run
    full=$("$gh" api "repos/OpenKingdoms/OpenKingdoms/commits/$want" --jq .sha 2> /dev/null) || return 1
    artifact_name="okengine-macos-arm64-$full"
    run=$("$gh" api "repos/$slug/actions/artifacts?name=$artifact_name&per_page=1" --jq '.artifacts[0].workflow_run.id // empty' 2> /dev/null)
    [ -n "$run" ] || return 1
    "$gh" run download "$run" -R "$slug" -n "$artifact_name" -D "$tmp/artifact" > /dev/null 2>&1 || return 1
    grep -q "okengine API $1 " "$tmp/artifact/VERSION-macos.txt" 2> /dev/null || return 1
    mv "$tmp/artifact/libokengine.dylib" "$tmp/libokengine.dylib"
}

python=""
find_python() {
    for p in /usr/bin/python3 python3 python py; do
        if command -v "$p" > /dev/null 2>&1 && "$p" -c "import hashlib, json" > /dev/null 2>&1; then python="$p"; return 0; fi
    done
    echo "mac-catch-up.sh needs python3, which comes with Xcode's command line tools: xcode-select --install"
    return 1
}

# Hashes, the editor's record of what it installed, and its import settings
# for the library, which it keeps in EngineInstaller.MacMeta.
py() {
"$python" - "$@" << 'EOF'
import hashlib, json, os, re, sys
cmd = sys.argv[1]
if cmd == 'hash':
    print(hashlib.sha256(open(sys.argv[2], 'rb').read()).hexdigest())
elif cmd.startswith('record-'):
    path, key = sys.argv[2], sys.argv[3]
    try:
        rec = json.load(open(path))
    except Exception:
        rec = {}
    if not isinstance(rec, dict):
        rec = {}
    if cmd == 'record-get':
        print(rec.get(key, ''))
        sys.exit(0)
    if cmd == 'record-set':
        rec[key] = sys.argv[4]
    elif key in rec:
        del rec[key]
    else:
        sys.exit(0)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', newline='\n') as f:
        f.write(json.dumps(rec, indent=2) + '\n')
elif cmd == 'meta':
    src, out = sys.argv[2], sys.argv[3]
    text = open(src, encoding='utf-8').read()
    guid = re.search(r'const string MacGuid = "([0-9a-f]+)"', text).group(1)
    if os.path.exists(out) and 'guid: ' + guid in open(out, encoding='utf-8').read():
        sys.exit(0)
    start = text.index('const string MacMeta =')
    block = text[start + len('const string MacMeta ='):text.index(';', start)]
    meta = ''.join(guid if name else lit.encode().decode('unicode_escape')
                   for lit, name in re.findall(r'"((?:[^"\\]|\\.)*)"|\b(MacGuid)\b', block))
    with open(out, 'w', newline='\n') as f:
        f.write(meta)
    print('wrote')
EOF
}

notify() {
    command -v osascript > /dev/null 2>&1 || return 0
    osascript -e "display notification \"$1\" with title \"Stars of Darien\"" > /dev/null 2>&1 || true
}

install_auto() {
    [ "$(uname)" = Darwin ] || { echo "install-auto sets up a launchd agent, which only a Mac has."; return 1; }
    mkdir -p "$(dirname "$agent")" "$(dirname "$log")"
    # Waking runs a missed hourly start, and resolv.conf changes when a
    # network comes up.
    cat > "$agent" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>$label</string>
    <key>ProgramArguments</key>
    <array>
        <string>/bin/bash</string>
        <string>$self</string>
        <string>auto</string>
    </array>
    <key>EnvironmentVariables</key>
    <dict>
        <key>PATH</key>
        <string>/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin:/usr/local/bin</string>
        <key>SOD_DIR</key>
        <string>$repo</string>
    </dict>
    <key>RunAtLoad</key>
    <true/>
    <key>StartCalendarInterval</key>
    <dict>
        <key>Minute</key>
        <integer>0</integer>
    </dict>
    <key>WatchPaths</key>
    <array>
        <string>/private/var/run/resolv.conf</string>
        <string>/Library/Preferences/SystemConfiguration</string>
    </array>
    <key>ThrottleInterval</key>
    <integer>120</integer>
    <key>ProcessType</key>
    <string>Background</string>
    <key>StandardOutPath</key>
    <string>$log</string>
    <key>StandardErrorPath</key>
    <string>$log</string>
</dict>
</plist>
EOF
    "$launchctl" bootout "gui/$(id -u)/$label" > /dev/null 2>&1 || true
    "$launchctl" bootstrap "gui/$(id -u)" "$agent" || { echo "launchctl could not load $agent."; return 1; }
    echo "Installed $agent."
    echo "It catches $repo up at login, on the hour, on waking and whenever a network comes up, and has started a first run now."
    echo "Log: $log"
    echo "Undo with: bash $self remove-auto"
}

remove_auto() {
    [ "$(uname)" = Darwin ] || { echo "remove-auto removes a launchd agent, which only a Mac has."; return 1; }
    "$launchctl" bootout "gui/$(id -u)/$label" > /dev/null 2>&1 || true
    rm -f "$agent"
    echo "Removed the automatic catch-up. bash $self still catches up by hand."
}

main "$@"
