#!/usr/bin/env python3
"""Check CP1.5 extraction against its base; this is source equivalence, not new native proof."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
LEX = re.compile(r'//[^\n]*|/\*[\s\S]*?\*/|\$?@"(?:""|[^"])*"|\$?"(?:\\.|[^"\\])*"|[A-Za-z_]\w*|[^\s]')


def tokens(text):
    return LEX.findall(text)


def body(text, name):
    match = re.search(r'^    (?:internal|private|public) [^\n]*\b' + re.escape(name) + r'\(', text, re.M)
    assert match, name
    opening, arrow = text.find('{', match.end()), text.find('=>', match.end())
    if arrow >= 0 and (opening < 0 or arrow < opening):
        return tokens(text[arrow + 2:text.index(';', arrow) + 1])
    level = 0
    for item in re.finditer(r'//[^\n]*|/\*[\s\S]*?\*/|\$?@"(?:""|[^"])*"|\$?"(?:\\.|[^"\\])*"|[{}]', text[opening:]):
        if item[0] == '{':
            level += 1
        elif item[0] == '}':
            level -= 1
            if level == 0:
                return tokens(text[opening:opening + item.end()])
    raise AssertionError(name)


def replace(sequence, old, new):
    old, new = tokens(old), tokens(new)
    count = 0
    result = []
    index = 0
    while index < len(sequence):
        if sequence[index:index + len(old)] == old:
            result.extend(new)
            index += len(old)
            count += 1
        else:
            result.append(sequence[index])
            index += 1
    assert count, old
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', default='7407c7197df9df4441307e9d822ea78042ae6d41')
    parser.add_argument('--head', help='Read a committed extraction instead of working files.')
    args = parser.parse_args()

    def read(path, revision=None):
        return subprocess.check_output(['git', 'show', revision + ':' + path], cwd=ROOT, text=True) if revision else (ROOT / path).read_text()

    report = {'base': args.base, 'checks': {}}

    def check(name, before, after):
        assert before == after, name
        report['checks'][name] = hashlib.sha256(json.dumps(before, ensure_ascii=False).encode()).hexdigest()

    session_path = 'src/OpenConquer.GameServer/World/Sessions/WorldSession.cs'
    old = read(session_path, args.base)
    owner = read(session_path, args.head)
    child = read('src/OpenConquer.GameServer/World/Sessions/WorldSessionGameplay.cs', args.head)
    moved = ['ProcessTradeQuit', 'ProcessWalkAsync', 'ProcessGhostTransitionAsync', 'ProcessJumpAsync',
             'ProcessChatAsync', 'ProcessAvatarActionAsync', 'ProcessPositionReportAsync',
             'ProcessClientStateApplied', 'CorrectRejectedMovementAsync', 'CreatePositionCorrection']
    rebound = {'Ownership', 'Profile', 'Map', 'ThrowIfInactive', 'QueueVisibilityPacket', 'WriteAsync', 'SendGameplayResponseAsync'}
    for name in moved:
        current = body(child, name)
        restored = []
        index = 0
        while index < len(current):
            if current[index:index + 2] == ['_session', '.'] and current[index + 2] in rebound:
                index += 2
            elif current[index] == '_session':
                restored.append('this')
                index += 1
            else:
                restored.append(current[index])
                index += 1
        check('gameplay.' + name, body(old, name), restored)
    for name in ['Activate', 'ThrowIfInactive', 'SynchronizeInitialStatusAsync', 'EnterVisibility', 'QueryVisibleEntity',
                 'QueueVisibilityPacket', 'QueueVisibilitySnapshot', 'QueuePortalArrival', 'ProcessPortalAsync',
                 'PumpVisibilityAsync', 'ReadAsync', 'SendGameplayResponseAsync', 'WriteAsync', 'DisposeAsync',
                 'SendOtherLoginNoticeAsync', 'FailConnectionAsync', 'OnPresenceRevoked', 'BeginDispose', 'DisposeCoreAsync']:
        check('session-owner.' + name, body(old, name), body(owner, name))
    constructor = replace(body(old, 'WorldSession'),
                          '_positionReconciler = new WorldPositionReconciler(bootstrap.Profile.Location.X, bootstrap.Profile.Location.Y);',
                          '_gameplay = new WorldSessionGameplay(this, runtime, visibility, bootstrap.Profile.Location.X, bootstrap.Profile.Location.Y);')
    check('session-constructor', constructor, body(owner, 'WorldSession'))
    rebound_location = replace(body(old, 'RebindPortalWorld'),
                               '_positionReconciler = new WorldPositionReconciler(persisted.Location.X, persisted.Location.Y); _walkPacer = new WorldWalkPacer();',
                               '_gameplay.RebindLocation(persisted.Location.X, persisted.Location.Y);')
    check('session-rebind-callsite', rebound_location, body(owner, 'RebindPortalWorld'))
    check('gameplay-rebind-state-order', tokens('{ _positionReconciler = new WorldPositionReconciler(x, y); _walkPacer = new WorldWalkPacer(); }'), body(child, 'RebindLocation'))
    check('gameplay-constructor-bindings', tokens('{ ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(runtime); _session = session; _runtime = runtime; _visibility = visibility; _positionReconciler = new WorldPositionReconciler(x, y); }'), body(child, 'WorldSessionGameplay'))
    for declaration in ['private WorldWalkPacer _walkPacer = new();',
                        'private readonly WorldJumpIngressLimiter _jumpIngressLimiter = new();',
                        'private readonly WorldAvatarActionLimiter _avatarActionLimiter = new();',
                        'private readonly WorldChatRateLimiter _chatRateLimiter = new();',
                        'private WorldPositionReconciler _positionReconciler;']:
        assert tokens(declaration) == tokens(re.search(re.escape(declaration), old)[0])
        assert tokens(declaration) == tokens(re.search(re.escape(declaration), child)[0])

    connection_path = 'src/OpenConquer.GameServer/Connections/GameConnectionSession.cs'
    old = read(connection_path, args.base)
    owner = read(connection_path, args.head)
    state = read('src/OpenConquer.GameServer/Connections/GameHandshakeProgress.cs', args.head)
    for name in ['EnterChallengeWrite', 'CompleteChallengeWrite', 'EnterKeyExchangeRead', 'CompleteKeyExchangeRead']:
        previous = body(old, name)
        prefix = tokens('{ lock (_protocolGate) { ThrowIfDisposed();')
        assert previous[:len(prefix)] == prefix and previous[-2:] == ['}', '}']
        check('handshake-guard.' + name, ['{'] + previous[len(prefix):-2] + ['}'], body(state, name))
        check('handshake-lock.' + name, tokens('{ lock (_protocolGate) { ThrowIfDisposed(); _handshake.' + name + '(); } }'), body(owner, name))
    complete = body(old, 'CompleteHandshake')
    first = complete.index('if')
    last = complete.index('GameSecuredFrameReader')
    guards = complete[first:last]
    check('handshake-completion-guards', ['{'] + guards + ['}'], body(state, 'RequireCompletion'))
    complete = complete[:first] + tokens('_handshake.RequireCompletion();') + complete[last:]
    complete = replace(complete, '_protocolState = ProtocolStateSecured;', '_handshake.MarkSecured();')
    check('handshake-codec-install-order', complete, body(owner, 'CompleteHandshake'))
    for name in ['GetSecuredFrameReader', 'GetSecuredFrameWriter']:
        expected = replace(body(old, name), '_protocolState != ProtocolStateSecured', '!_handshake.IsSecured')
        check('handshake-secured-access.' + name, expected, body(owner, name))
    expected = replace(body(old, 'ResetKeyExchangeReadAfterCancellation'),
                       '_protocolState == ProtocolStateBootstrap && _keyExchangeState == KeyExchangeStateActive', '_handshake.CanResetKeyExchangeRead')
    expected = replace(expected, '_keyExchangeState = KeyExchangeStateNotStarted;', '_handshake.ResetKeyExchangeRead();')
    check('handshake-cancellation-and-dispose-order', expected, body(owner, 'ResetKeyExchangeReadAfterCancellation'))
    expected = replace(body(old, 'MarkProtocolTerminal'), '_protocolState = ProtocolStateTerminal;', '_handshake.MarkTerminal();')
    check('handshake-terminal-lock', expected, body(owner, 'MarkProtocolTerminal'))
    for name, expression in [('MarkTerminal', '_protocolState = ProtocolStateTerminal;'), ('MarkSecured', '_protocolState = ProtocolStateSecured;'), ('ResetKeyExchangeRead', '_keyExchangeState = KeyExchangeStateNotStarted;')]:
        check('handshake-state-write.' + name, tokens(expression), body(state, name))
    for name in ['OpenAsync', 'SendHandshakeChallengeAsync', 'ReadClientKeyExchangeResponseAsync', 'ReadAsync',
                 'WriteAsync', 'WriteBatchAsync', 'WriteCoreAsync', 'DisposeAsync', 'StartDispose', 'WriteHandshakeChallengeCoreAsync',
                 'DisposeCoreAsync', 'EnterOperation', 'ExitOperation', 'CaptureOperationDrainTask', 'CreateInputPipe', 'CreateOutputPipe',
                 'ThrowIfInputPumpFaultedAsync', 'ThrowForOutputPumpCompletionAsync', 'CreateOpenFailure', 'ObservePumpShutdownAsync',
                 'IsPeerDisconnect', 'ThrowIfDisposed']:
        check('connection-owner.' + name, body(old, name), body(owner, name))
    constants = re.findall(r'    private const int (\w+) = (\d+);', old)
    check('handshake-constants', constants, re.findall(r'    private const int (\w+) = (\d+);', state))
    for field in ['_protocolState', '_challengeState', '_keyExchangeState']:
        declaration = r'private int ' + field + r' = \w+;'
        check('handshake-initial-state.' + field, tokens(re.search(declaration, old)[0]), tokens(re.search(declaration, state)[0]))
    assert tokens(re.search(r'public bool IsSecured =>[^;]+;', state)[0]) == tokens('public bool IsSecured => _protocolState == ProtocolStateSecured;')
    assert tokens(re.search(r'public bool CanResetKeyExchangeRead =>[^;]+;', state)[0]) == tokens('public bool CanResetKeyExchangeRead => _protocolState == ProtocolStateBootstrap && _keyExchangeState == KeyExchangeStateActive;')
    for path in ['src/OpenConquer.GameServer/World/Execution/WorldRuntime.cs',
                 'src/OpenConquer.Infrastructure/Persistence/Accounts/Mutations/AccountMutationStore.cs']:
        check('retained.' + path, read(path, args.base), read(path, args.head))
    diff = ['git', 'diff', args.base]
    if args.head:
        diff.append(args.head)
    diff += ['--', 'tests']
    assert not subprocess.check_output(diff, cwd=ROOT), 'Existing tests changed.'
    report['existingTestsChanged'] = False
    print(json.dumps(report, indent=2) + '\n', end='')


if __name__ == '__main__':
    main()
