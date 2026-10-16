import SwiftUI

// MARK: - 2. Onboarding Screen
struct OnboardingView: View {
    let onNext: () -> Void
    let onBack: () -> Void
    @State private var page = 0

    let pages: [(eyebrow: LocalizedStringKey, title: LocalizedStringKey, body: LocalizedStringKey)] = [
        (AppStrings.Onboarding.welcomeEyebrow, AppStrings.Onboarding.welcomeTitle, AppStrings.Onboarding.welcomeBody),
        (AppStrings.Onboarding.avatarEyebrow, AppStrings.Onboarding.avatarTitle, AppStrings.Onboarding.avatarBody),
        (AppStrings.Onboarding.syncEyebrow, AppStrings.Onboarding.syncTitle, AppStrings.Onboarding.syncBody),
    ]

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {


                // Hero
                ZStack(alignment: .bottom) {
                    LinearGradient(
                        colors: [Color.arCard, Color.arBG],
                        startPoint: .top, endPoint: .bottom
                    )
                    .frame(height: 300)
                    .clipShape(RoundedRectangle(cornerRadius: 32))
                    .padding(.horizontal, 20)

                    Ellipse()
                        .fill(Color.arYellow.opacity(0.07))
                        .frame(width: 260, height: 80)
                        .blur(radius: 24)
                        .offset(y: -20)

                    Image(systemName: "figure.run")
                        .font(.system(size: 100))
                        .foregroundColor(.white.opacity(0.9))
                        .offset(y: -40)

                    LinearGradient(
                        colors: [.clear, Color.arBG.opacity(0.65)],
                        startPoint: .top, endPoint: .bottom
                    )
                    .frame(height: 300)
                    .padding(.horizontal, 20)
                }

                Spacer().frame(height: 28)

                // Text pages
                TabView(selection: $page) {
                    ForEach(pages.indices, id: \.self) { i in
                        VStack(alignment: .leading, spacing: 10) {
                            ARLabel(text: pages[i].eyebrow)
                            Text(pages[i].title)
                                .font(.system(size: 28, weight: .bold))
                                .foregroundColor(.white)
                                .lineSpacing(2)
                            Text(pages[i].body)
                                .font(.system(size: 15))
                                .foregroundColor(.arGrayText)
                                .lineSpacing(5)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 28)
                        .tag(i)
                    }
                }
                .tabViewStyle(.page(indexDisplayMode: .never))
                .frame(height: 155)

                // Progress dots
                HStack(spacing: 5) {
                    ForEach(pages.indices, id: \.self) { i in
                        Capsule()
                            .fill(i == page ? Color.arYellow : Color.arBorder)
                            .frame(width: i == page ? 22 : 6, height: 6)
                            .animation(.spring(response: 0.35, dampingFraction: 0.7), value: page)
                    }
                }
                .padding(.top, 10)
                .padding(.leading, 28)

                Spacer()

                ARButton(page < pages.count - 1 ? AppStrings.Common.next : AppStrings.Onboarding.start, icon: "arrow.right") {
                    if page < pages.count - 1 {
                        withAnimation { page += 1 }
                    } else {
                        onNext()
                    }
                }
                .padding(.horizontal, 24)
                .padding(.bottom, 52)
            }
        }
    }
}
