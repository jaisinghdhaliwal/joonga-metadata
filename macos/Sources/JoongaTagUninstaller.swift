import AppKit

enum JoongaTagUninstaller {
    static func schedule() throws {
        let bundle = Bundle.main.bundleURL
        let domain = JoongaTagStorage.domain
        let files = FileManager.default
        guard Bundle.main.bundleIdentifier == domain, bundle.pathExtension == "app",
              !bundle.path.contains("/AppTranslocation/"),
              files.isWritableFile(atPath: bundle.deletingLastPathComponent().path) else {
            throw MacAppUpdater.failure("Joonga Tag needs write access to its app folder. Move it to a writable Applications folder and try again.")
        }
        guard NSRunningApplication.runningApplications(withBundleIdentifier: domain).count <= 1 else {
            throw MacAppUpdater.failure("Quit the other Joonga Tag windows before uninstalling.")
        }
        let library = files.homeDirectoryForCurrentUser.appendingPathComponent("Library")
        let paths = [bundle.path, JoongaTagStorage.folder.path] + [
            "Preferences/\(domain).plist", "Caches/\(domain)", "HTTPStorages/\(domain)",
            "HTTPStorages/\(domain).binarycookies", "Saved Application State/\(domain).savedState",
            "WebKit/\(domain)"
        ].map { library.appendingPathComponent($0).path }
        let helper = Process()
        helper.executableURL = URL(fileURLWithPath: "/bin/sh")
        helper.arguments = ["-c", """
            pid="$1"; domain="$2"; shift 2
            attempts=0
            while kill -0 "$pid" 2>/dev/null; do
              attempts=$((attempts + 1))
              [ "$attempts" -lt 30 ] || exit 1
              sleep 1
            done
            /usr/bin/defaults delete "$domain" >/dev/null 2>&1 || true
            failed=0
            for target in "$@"; do
              /bin/rm -rf -- "$target" || failed=1
            done
            if [ "$failed" -ne 0 ]; then
              /usr/bin/osascript -e 'display alert "Some Joonga Tag files could not be removed."'
            fi
            """, "joonga-uninstall", String(ProcessInfo.processInfo.processIdentifier), domain] + paths
        try helper.run()
    }
}
