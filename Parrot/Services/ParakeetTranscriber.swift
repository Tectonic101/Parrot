import FluidAudio
import Foundation

/// NVIDIA Parakeet TDT 0.6B v3 via FluidAudio: 25 European languages on the
/// Neural Engine, about 0.5 GB. FluidAudio's ASR API stays in this file.
/// (Write `Language`, not `FluidAudio.Language`: FluidAudio also declares a
/// `struct FluidAudio`, which shadows the module name.)
final class ParakeetTranscriber: Sendable {
    static let modelID = "parakeet-v3"
    static let displayName = "Parakeet v3"
}
