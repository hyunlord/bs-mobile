import AppKit
import Foundation
import Darwin

// NSWorkspace activation metadata does not inspect window contents or require
// Screen Recording / Accessibility permission. Run outside the measured game.
let arguments = CommandLine.arguments
if arguments.count != 3 || Double(arguments[2]) == nil {
    fputs("usage: observe-foreground <output.jsonl> <maximum-seconds>\n", stderr)
    exit(2)
}
let destination = arguments[1]
guard !FileManager.default.fileExists(atPath: destination),
      FileManager.default.createFile(atPath: destination, contents: nil),
      let output = FileHandle(forWritingAtPath: destination),
      let duration = Double(arguments[2]), duration > 0 else {
    fputs("output must be new and duration must be positive\n", stderr)
    exit(2)
}
func record(_ kind: String, _ application: NSRunningApplication?) {
    let value: [String: Any] = [
        "event": kind,
        "utcUnixMilliseconds": Date().timeIntervalSince1970 * 1000,
        "observerUptimeSeconds": ProcessInfo.processInfo.systemUptime,
        "name": application?.localizedName ?? "unavailable",
        "bundleIdentifier": application?.bundleIdentifier ?? "unavailable",
        "pid": application?.processIdentifier ?? -1,
        "source": "NSWorkspace.didActivateApplicationNotification"
    ]
    do {
        var bytes = try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])
        bytes.append(0x0a)
        try output.write(contentsOf: bytes)
    } catch {
        fputs("foreground observer write failed\n", stderr)
        exit(1)
    }
}
record("initial", NSWorkspace.shared.frontmostApplication)
let observer = NSWorkspace.shared.notificationCenter.addObserver(
    forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main
) { notification in
    record("activated", notification.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication)
}
func finish() {
    record("observer-end", NSWorkspace.shared.frontmostApplication)
    NSWorkspace.shared.notificationCenter.removeObserver(observer)
    try? output.close()
    exit(0)
}
signal(SIGTERM, SIG_IGN)
let termination = DispatchSource.makeSignalSource(signal: SIGTERM, queue: .main)
termination.setEventHandler { finish() }
termination.resume()
DispatchQueue.main.asyncAfter(deadline: .now() + duration) { finish() }
RunLoop.main.run()
