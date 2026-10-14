import Foundation
#if canImport(AVFoundation)
import AVFoundation
#endif
#if canImport(UIKit)
import UIKit
#endif

/// 特定音声＆優先度制御 (企画書 4.3):
/// 音声警告の対象は「赤信号」「交差点」のみ。複数の警告が重複した場合は
/// TTC(衝突猶予時間)が短いものを優先し、発話中でも割り込む。
///
/// 発火元: Unityからの VoiceAlert イベント(現状はエディタシミュレーション。
/// 実運用の検知ソースは地図データ連携が必要 — HANDOVER.md 未完了事項参照)。
final class VoiceAlertSpeaker: NSObject {

    static let shared = VoiceAlertSpeaker()

#if canImport(AVFoundation)
    private let synthesizer = AVSpeechSynthesizer()
    private var speakingTtc: Double = .infinity

    private override init() {
        super.init()
        synthesizer.delegate = self
    }

    /// kind: "Signal"(赤信号) / "Intersection"(交差点)。それ以外は対象外として無視。
    func speak(kind: String, ttcSeconds: Double) {
        let phrase: String
        switch kind {
        case "Signal":
            phrase = "前方、赤信号です。停止してください。"
        case "Intersection":
            phrase = "交差点に接近しています。左右を確認してください。"
        default:
            return // 企画書4.3: 音声警告対象は赤信号・交差点のみ
        }

        // 優先度制御: 発話中はTTCがより短い警告のみ割り込み許可
        if synthesizer.isSpeaking {
            guard ttcSeconds < speakingTtc else { return }
            synthesizer.stopSpeaking(at: .immediate)
        }
        speakingTtc = ttcSeconds

        let utterance = AVSpeechUtterance(string: phrase)
        utterance.voice = AVSpeechSynthesisVoice(language: "ja-JP")
        utterance.rate = 0.52
        utterance.volume = 1.0
        synthesizer.speak(utterance)

        // 企画書4.3 マルチモーダル通知: 信号は長め振動
        if kind == "Signal" {
            playLongVibration()
        }
    }

    private func playLongVibration() {
#if canImport(UIKit) && !targetEnvironment(simulator)
        let generator = UINotificationFeedbackGenerator()
        generator.notificationOccurred(.warning)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) {
            generator.notificationOccurred(.warning)
        }
#endif
    }
#else
    private override init() { super.init() }
    func speak(kind: String, ttcSeconds: Double) {}
#endif
}

#if canImport(AVFoundation)
extension VoiceAlertSpeaker: AVSpeechSynthesizerDelegate {
    func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer,
                           didFinish utterance: AVSpeechUtterance) {
        speakingTtc = .infinity
    }
    func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer,
                           didCancel utterance: AVSpeechUtterance) {
        // 割り込み時は後続のspeak()がspeakingTtcを更新済み
    }
}
#endif
