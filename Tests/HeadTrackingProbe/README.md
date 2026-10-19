# Head tracking probe — 実機確認手順

追加画面だけが専用の CMHeadphoneMotionManager / CMMotionManager を所有する。
Unity・既存のセッション・既存のセンサーサービスへの参照や送信はない。
既存 DevModeView の変更は NavigationLink の追加のみ。

## Xcode / 実機

1. 通常の iOS アプリを Xcode で開き、署名済みの実機を選んで Run。
   PBXFileSystemSynchronizedRootGroup により追加 Swift ファイルは自動で対象になる。
   Unity を含む既存ビルドが失敗する場合は、このプローブとは別のビルド問題として確認する。
2. AirPods Pro 3 を接続・装着。開発者モード →「頭部姿勢の計測（試作）」を開く。
   初回のモーション許可に応答し、許可状態・接続・利用可能状態を確認。
   拒否した場合は iOS 設定で許可を変更して画面を開き直す。
3. iPhone を胸に縦固定し背面カメラを前方へ。頭を正面にして前方軸を選ぶ。
   既定 −Z は仮。X/Y/Z と反転を試し、水平投影が有効で左右の回転を追える軸を選ぶ。
4. 「ゼロ合わせ」。頭だけ右へ約30度 → 首角度が約 +30度、左へ → 約 −30度。
   頭と体を同じだけ回す → 首角度がほぼ0度。iPhoneだけ右へ → 首角度は負。
   ±180度の境界を越えても値が −180以上180未満に収まることを確認。
5. 上下を見る/首を傾げる。投影が有効な範囲では水平角を確認。
   前方軸が鉛直に近い場合は「—」と理由を表示する。任意の0度にはしない。
6. 軸とゼロを決めて「記録開始」。件数が増えることを確認。
   「記録停止」→「CSVを共有」(ShareLink)。受信1回につき1行、ヘッダー8列を確認。
7. 記録中に画面ロックして10秒待つ。解除後、記録中状態・件数・途切れ件数を確認。
   ホーム画面へ移動するケースも別に試す。Background Mode追加や延命処理はない。
   間隔が1秒を超えた途切れは次のサンプル到着時に1件加算する。
   最後のサンプル以降まったく復帰しない停止は、件数停止・Hz低下で確認する。
8. AirPodsを切断して再接続。接続状態更新と途切れを確認。
   再接続で基準は無効になる。記録停止→再ゼロ合わせ→新しいCSVで続ける。
9. 画面から戻ると専用センサーと記録が止まることを確認。
   元の開発者画面・走行・Unity連携の挙動が変わっていないことも確認する。

各ケースで端末/iOS/AirPodsファームウェア、選択軸、装着状態、実際の結果とCSVを残す。
実機では未検証。特に左右符号と行列の実機座標対応を上記4で確認する。

## 計算と統計の定義

- Core Motion の rotationMatrix を転置してローカル軸を基準座標に変換する。
- 同じ変換で gravity を基準座標に移し、反対向きを up とする。
- `horizontal = forward − dot(forward, up) × up`。
- 基準X軸を水平面に投影した x と `y = up × x` に対し、
  `yaw = −atan2(dot(horizontal,y), dot(horizontal,x)) × 180/π`。
  上から見た時計回り(右旋回)をプラスとする。
- iPhone forward は −Z、AirPods は選択した軸。attitude.yaw は不使用。
- 首角度 = wrap((head − headZero) − (phone − phoneZero))。wrap 範囲は [−180,180)。
- 重力または水平投影の長さが0.001以下/非有限なら無効。
- iPhoneサンプルが受信時より0.5秒以上古い場合は首角度を無効にする。
- Hz は直近5秒の AirPods timestamp の件数÷5。開始5秒未満も分母は5。
- 最大間隔と母標準偏差は直近5秒に到着点timestampが入る正のサンプル間隔。
  1秒超の間隔も含む。途切れ件数は画面を開いてからの累積。
- 遅延参考値はメインキューのコールバック開始時のsystemUptime − timestamp。
  指定された参考式であり、BLE全経路の遅延測定ではない。UI負荷・CSV書込み・
  コールバックの滞留が参考値に影響する。

## CSV

Documents/HeadTracking/HeadTracking_開始日時_UUID.csv。

`sampleTime,receivedTime,headYaw,headPitch,headRoll,phoneYaw,neckYaw,connected`

時刻は起動からの秒、角度は度、小数点は常に `.`。
headPitch/headRoll は Core Motion の生のオイラー角(前方軸選択には連動しない)。
phoneYaw は最新の利用可能値。neckYaw は基準未設定・古いiPhone値・無効投影時に空欄。
connected はdelegateの接続1/切断0/未通知は空欄。切断中はサンプルがないため行は増えない。
記録開始ごとに新しいファイル。記録中の軸変更・ゼロ変更・共有はUIで禁止。
停止時に同期・クローズし、書込み失敗は画面表示して記録停止。
新しいBackground Mode、バックグラウンドタスク、idle timer変更は行わない。

## 不確実な点

- AirPods Pro 3 の実際の前方軸と装着/イヤーバッド切替時の姿勢基準は実機確認が必要。
- 軸を反転すると方位が180度ずれるが、ゼロ合わせ後の旋回符号は通常変わらない。
  右旋回が負の場合、反転軸で修正した扱いにせず、装着・行列座標を再検証する。
- 別々の任意基準をゼロ合わせで揃えるだけなので、時間経過によるドリフトや
  AirPods内部の再センタリングは補償できない。
- 頭と胸のサンプルは同期されていない。高速回転では最新iPhone値との時間差が誤差になる。
- ロック継続、ファイル保護による書込み制限、AirPodsの実配信レートは現状のOS設定に依存。
  アプリ終了で記録は止まる。この画面はバックグラウンド継続を保証しない。

Apple API references:
- https://developer.apple.com/documentation/coremotion/cmheadphonemotionmanager
- https://developer.apple.com/documentation/coremotion/cmheadphonemotionmanagerdelegate
- https://developer.apple.com/documentation/coremotion/cmattitude/rotationmatrix

## 再実行可能な計算テスト

リポジトリルートから:

```sh
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer xcrun swiftc \
  ios/AR_Runner_UI/AR_Runner_UI/Services/HeadTrackingProbeMath.swift \
  Tests/HeadTrackingProbe/main.swift -o /tmp/head-probe-tests
/tmp/head-probe-tests
```

人工的な行列で右/左、胸固定の−Z、傾き、鉛直無効、wrap、体との相殺、統計を確認。
15チェック通過。追加サービス/画面はiOS SDKによる型検査も通過。
`tools/verify.sh` は dotnet 未導入のため最初の C# compile で停止。
実機・CSV共有・全体ビルドの成功を意味しない。
