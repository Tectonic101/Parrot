import FluidAudio
import Foundation

/// NVIDIA Parakeet TDT 0.6B v3 via FluidAudio: 25 European languages on the
/// Neural Engine, about 0.5 GB. FluidAudio's ASR API stays in this file.
/// (Write `Language`, not `FluidAudio.Language`: FluidAudio also declares a
/// `struct FluidAudio`, which shadows the module name.)
final class ParakeetTranscriber: Sendable {
    static let modelID = "parakeet-v3"
    static let displayName = "Parakeet v3"

    private let manager: AsrManager

    private init(manager: AsrManager) {
        self.manager = manager
    }

    static var isDownloaded: Bool {
        AsrModels.modelsExist(at: AsrModels.defaultCacheDirectory(for: .v3), version: .v3)
    }

    /// Downloads on first use (about 0.5 GB), then loads onto the Neural Engine.
    static func load(progress: @escaping @Sendable (Double) -> Void) async throws -> ParakeetTranscriber {
        let models = try await AsrModels.downloadAndLoad(version: .v3) { progress($0.fractionCompleted) }
        let manager = AsrManager(config: .default)
        try await manager.loadModels(models)
        return ParakeetTranscriber(manager: manager)
    }

    /// One utterance (16 kHz mono). `language`: the ISO code heard, which
    /// Parakeet uses as a script filter (Latin, Cyrillic, Greek); nil = any.
    func transcribe(_ samples: [Float], language: String?) async throws -> String {
        try await transcribeScored(samples, language: language).text
    }

    /// The same, with Parakeet's own 0-1 confidence for the line.
    func transcribeScored(_ samples: [Float], language: String?) async throws -> (text: String, confidence: Float) {
        var state = TdtDecoderState.make(decoderLayers: await manager.decoderLayerCount)
        let result = try await manager.transcribe(samples, decoderState: &state,
                                                  language: language.flatMap(Language.init(rawValue:)))
        return (result.text, result.confidence)
    }
}
