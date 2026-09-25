# ARCore Scene Semantics — 屋外路面の分類

## 何のために入れるのか

ARKitの面分類(`XRMeshClassification` / `PlaneClassifications`)の語彙は**屋内語**で、
Floor / Wall / Ceiling / Table / Seat / Window / Door しか無い。**road が無い。**
`SurfaceSemanticMath` のコメントにもそう書いてある —

> ARKitに「road」が無いため、Unknown / Other は従来の法線・高さ判定へ残し、屋外路面を一律に捨てない

つまり屋内の接地は「分類で確実に」判断できるのに、**第1期が実際に走る屋外は
法線と高さという幾何だけ**で判断している。ARCore Scene Semantics は
Road / Sidewalk / Terrain を直接返すので、その穴だけを埋める。

Geospatial API と違い**推論は端末内で完結**するので、VPSのカバレッジも
Street View のマッピングも関係ない。陸上トラックでも動く(ラベルが何になるかは別問題、後述)。

---

## 実装の形 — ベンダー境界を1か所に閉じてある

| 要素 | 役割 | ARCore依存 |
|---|---|---|
| `SurfaceSemanticMath.FromArcoreLabel(int)` | ラベル値(0〜11)→ `SurfaceSemantic` | **無し**(数値で受ける) |
| `SurfaceSemanticMath.Combine(geometric, image)` | 3D面分類と画像分類の合成 | 無し |
| `SurfaceSemanticMath.TryViewportToPixel(...)` / `IsImageSampleFresh(...)` | 画素位置・鮮度の判定 | 無し |
| `OutdoorSemanticClassifier` | セマンティック画像の取得と点の問い合わせ | **`#if ARCORE_EXTENSIONS` の中だけ** |
| `ARMeshSemanticSurface.FromRaycastHit(hit, outdoor)` | 既存の解決経路への合流点 | 無し |

判断ロジックは全部 Unity非依存の純ロジックに置いてあるので、**パッケージを入れなくても
`dotnet test` で検証できる**(38件)。ARCoreに触れるのは
`OutdoorSemanticClassifier` の2メソッドだけ。

### 設計上の不変条件

1. **3D面分類は画像分類に上書きされない。** メッシュの面分類はその三角形そのものの分類で、
   画像分類は2D投影からの推定にすぎない。画像が効くのは3D側が Unknown / Other のときだけ。
   → E2E: `semantics: a 3D face classification is never overridden by the image label`
2. **判らなければ従来どおり。** 未知のラベル・低信頼度・画面外・カメラ後方・サンプル切れは
   すべて「分類しない」に落ち、接地判定は従来の幾何のまま動く。
3. **60fpsの描画経路にML推論を載せない。** セマンティック画像の取得は既定5Hz。
   取得時に1度だけバイト配列へ写し、点の問い合わせは配列参照だけで済ませる。
4. **水面は地面にしない。** 幾何的には水平な地面に見えるが、そこへアバターを置くと
   ランナーを水面へ誘導することになる。ただし足踏み停止はさせない(水たまりで走行が止まる)。
5. **空は地面でも障害物でもない。**

---

## 導入手順(Mac / 実機側)

現在はパッケージ未導入で、`OutdoorSemanticClassifier` は休眠している
(E2Eが「休眠していること」と「休眠中は接地判定が変わらないこと」を検証済み)。

1. **パッケージを追加** — Unity の Package Manager → `Add package from git URL...`

   ```
   https://github.com/google-ar/arcore-unity-extensions.git#arf6
   ```

   `arf6` ブランチが AR Foundation 6 系対応(v1.48.0以降)。本プロジェクトは
   `com.unity.xr.arfoundation` 6.4.2 なのでこちら。

2. **`ARSemanticManager` をARセッションへ追加** し、セッション設定の `SemanticMode` を
   `Enabled` にする。`OutdoorSemanticClassifier` は `FindFirstObjectByType` で拾うので、
   シーン内のどこにあってもよい。

3. **スクリプティング定義シンボルに `ARCORE_EXTENSIONS` を追加**
   (Project Settings → Player → iOS → Scripting Define Symbols)。
   これが無い間はコードごと `#else` 側に落ちて休眠する。**外せば即座に元の挙動へ戻る。**

4. **iOS側の依存** — ARCore iOS SDK のフレームワーク解決(CocoaPods、または v1.53.0 以降の
   Swift Package Manager 対応)。UaaL構成なので `ios/UnityExport` 再生成後に
   `SWIFT_INTEGRATION.md` ②の再リンクと同じ手当てが要る。

5. **Cloud プロジェクト / APIキー** — Scene Semantics の推論は端末内で完結するため、
   Geospatial API や Cloud Anchors と違ってクラウド呼び出しは無いはず。
   ただし ARCore Extensions は共通の設定資産(`ARCoreExtensionsConfig`)を持つので、
   **導入時にキー要求が出ないことを実際に確認すること**(出るようなら §1.2 の
   「外部サーバーはスコープ外」に触れるためチーム判断へ回す)。

---

## 実機で確認が必要なこと

| # | 確認内容 | なぜ |
|---|---|---|
| 1 | **陸上トラックの合成路面が何にラベルされるか** | ARCoreの定義では ROAD は「車が走れる路面」、TERRAIN は「草・土・砂」。タータントラックはどちらでもない。UNLABELED に落ちる可能性が高い — その場合は Unknown 扱いで従来経路へ戻るだけなので**安全側に倒れる**が、「効果が無い」ことは確認しておく |
| 2 | **セマンティック画像とビューポートの向きの一致** | セマンティック画像はカメラ画像座標系。上下が逆なら `flipVertically` を有効化する。ずれていると足元を見ているつもりで空をサンプルする |
| 3 | **端末が対応しているか** | 公式は「Depth API と同じ対応端末リスト」とだけ言っている。`IsSemanticModeSupported` の戻り値をログで確認(`[SEMANTICS]` 行に出る) |
| 4 | **ポートレート向きの前提** | 公式に「ポートレート専用、ランドスケープではラベル品質を保証しない」とある。胸マウントの実際の保持向きで品質が出るか |
| 5 | **M2Pへの影響** | 5Hzに落としてあるが、ML推論はGPUを食う。§10の20ms予算が崩れないことをCSVで確認する |

---

## 使わない判断もあり得る

1 の結果次第では、トラック(第1期の検証場所)で効果がゼロということが有り得る。
その場合でも**市街地・公園コースでは効く**ので、第2期の屋外汎用化まで
定義シンボルを外したまま寝かせておけばよい。コストはコード量だけで、実行時ゼロ。

---

## 出典

- [Scene Semantics — ARCore](https://developers.google.com/ar/develop/scene-semantics)(屋外専用・ポートレート専用・対応端末はDepth APIと同じ)
- [ArSemanticLabel 定数](https://developers.google.com/ar/reference/c/group/ar-semantic-label)(UNLABELED=0 〜 WATER=11)
- [ARSemanticManager クラス](https://developers.google.com/ar/reference/unity-arf/class/Google/XR/ARCoreExtensions/ARSemanticManager)(`TryGetSemanticTexture` / `TryGetSemanticConfidenceTexture` / `IsSemanticModeSupported`)
- [ARCore Extensions リリース](https://github.com/google-ar/arcore-unity-extensions/releases)(AR Foundation 6 は `arf6` / v1.48.0以降)
