import SwiftUI

// MARK: - Design Tokens
extension Color {
    static let arYellow    = Color(red: 0.82, green: 0.95, blue: 0.10) // #D2F21A
    static let arBG        = Color(red: 0.08, green: 0.08, blue: 0.08) // #141414
    static let arCard      = Color(red: 0.13, green: 0.13, blue: 0.13) // #212121
    static let arBorder    = Color(red: 0.22, green: 0.22, blue: 0.22) // #383838
    static let arGrayText  = Color(red: 0.55, green: 0.55, blue: 0.55)
    static let arYellowDim = Color(red: 0.82, green: 0.95, blue: 0.10).opacity(0.12)
}

// MARK: - Primary Button
struct ARButton: View {
    let title: String
    let icon: String?
    let style: ButtonStyle
    let action: () -> Void

    enum ButtonStyle { case primary, secondary, icon }

    init(_ title: String, icon: String? = nil, style: ButtonStyle = .primary, action: @escaping () -> Void) {
        self.title = title; self.icon = icon; self.style = style; self.action = action
    }

    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                Text(title)
                    .font(.system(size: 16, weight: .bold))
                if let icon {
                    Image(systemName: icon)
                        .font(.system(size: 13, weight: .bold))
                }
            }
            .frame(maxWidth: .infinity)
            .frame(height: 54)
            .background(
                style == .primary
                    ? AnyShapeStyle(Color.arYellow)
                    : AnyShapeStyle(Color.arCard)
            )
            .foregroundColor(style == .primary ? .black : .white)
            .clipShape(RoundedRectangle(cornerRadius: 27))
            .overlay(
                RoundedRectangle(cornerRadius: 27)
                    .stroke(style == .secondary ? Color.arBorder : .clear, lineWidth: 1)
            )
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Screen Container
struct ARScreen<Content: View>: View {
    let content: Content
    init(@ViewBuilder content: () -> Content) { self.content = content() }
    var body: some View {
        ZStack {
            Color.arBG.ignoresSafeArea()
            content
        }
    }
}

// MARK: - Eyebrow Label  (e.g.  "ACTIVITY" in yellow tracking)
struct ARLabel: View {
    let text: String
    var body: some View {
        Text(text)
            .font(.system(size: 11, weight: .bold))
            .foregroundColor(.arYellow)
            .tracking(2.5)
    }
}

// MARK: - Divider
struct ARDivider: View {
    var body: some View {
        Rectangle()
            .fill(Color.arBorder)
            .frame(height: 0.5)
    }
}

// MARK: - Bottom Tab Bar
struct TabBarView: View {
    @Binding var selected: Int
    let items: [(icon: String, label: String)]

    var body: some View {
        HStack(spacing: 0) {
            ForEach(items.indices, id: \.self) { i in
                Button {
                    selected = i
                } label: {
                    VStack(spacing: 4) {
                        Image(systemName: items[i].icon)
                            .font(.system(size: 22))
                        Text(items[i].label)
                            .font(.system(size: 10))
                    }
                    .frame(maxWidth: .infinity)
                    .foregroundColor(selected == i ? .arYellow : .arGrayText)
                }
            }
        }
        .padding(.horizontal, 16)
        .padding(.top, 12)
        .padding(.bottom, 24)
        .background(Color.arCard)
        .overlay(ARDivider(), alignment: .top)
    }
}

// MARK: - Stat Badge
struct StatBadge: View {
    let value: String
    let unit: String
    let label: String

    var body: some View {
        VStack(spacing: 2) {
            HStack(alignment: .lastTextBaseline, spacing: 2) {
                Text(value).font(.system(size: 28, weight: .bold)).foregroundColor(.white)
                Text(unit).font(.system(size: 13)).foregroundColor(.arGrayText)
            }
            Text(label).font(.system(size: 12)).foregroundColor(.arGrayText)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 16)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 16))
    }
}
