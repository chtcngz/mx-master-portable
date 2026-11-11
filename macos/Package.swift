// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "MXMasterPortable",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "MXMasterPortable",
            path: "Sources/MXMasterPortable",
            linkerSettings: [
                .linkedFramework("IOKit"),
                .linkedFramework("AppKit"),
                .linkedFramework("ApplicationServices")
            ]
        )
    ]
)
