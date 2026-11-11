import SwiftUI

struct MainView: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text("MX Master Portable").font(.title2.bold())
                Text("MX Master 3S / 4  •  Bluetooth ve Bolt  •  Tuş atamaları")
                    .foregroundStyle(.secondary)
            }

            HStack(spacing: 8) {
                Button("Cihazları tara") { Task { await model.scan() } }
                    .disabled(!model.canScan)
                Button("Atamaları uygula") { Task { await model.apply() } }
                    .buttonStyle(.borderedProminent)
                    .disabled(!model.canApply)
                Button("Durdur / geri al") { Task { await model.stop() } }
                    .disabled(!model.canStop)
                Button("Önceki ayarı kurtar") { Task { await model.recover() } }
                    .disabled(!model.canRecover)
                Spacer()
                Label(model.state, systemImage: model.active ? "checkmark.circle.fill" : "circle")
                    .foregroundStyle(model.active ? Color.green : Color.secondary)
            }

            Picker("Bağlı cihaz", selection: $model.selectedId) {
                if model.sessions.isEmpty {
                    Text("Cihaz bulunamadı").tag(UUID?.none)
                }
                ForEach(model.sessions) { session in
                    Text(session.title).tag(Optional(session.id))
                }
            }
            .disabled(!model.canEdit)

            GroupBox {
                assignments
            } label: {
                Text("Tuşlar ve atamalar").bold()
            }

            Toggle("Fare geri/ileri/orta tuşlarını bütün farelerde yakala (isteğe bağlı, cihaz bazlı değil)",
                   isOn: $model.allowGlobal)
                .disabled(!model.canEdit)
            Toggle("Zaten yönlendirilmiş HID++ tuşlarını dinle (diğer uygulama da çalışabilir)",
                   isOn: $model.reuseDiverted)
                .disabled(!model.canEdit)

            GroupBox {
                logView
            } label: {
                Text("Tanılama / olay günlüğü").bold()
            }

            Text("Pencere kapanınca menü çubuğunda çalışır. Tam çıkış: menü çubuğu simgesi → Uygulamadan çık. " +
                 "Logi Options+ açıksa atamalar çakışabilir.")
                .font(.callout)
                .foregroundStyle(.orange)
        }
        .padding(20)
    }

    private var assignments: some View {
        Grid(alignment: .leading, horizontalSpacing: 18, verticalSpacing: 6) {
            GridRow {
                Text("Erişim")
                Text("Tuş / Kontrol")
                Text("Atanan işlem")
            }
            .font(.subheadline.bold())
            .foregroundStyle(.secondary)
            Divider()
            ForEach(model.rows) { row in
                GridRow {
                    Text(row.source).foregroundStyle(.secondary)
                    Text(row.name)
                    if row.editable {
                        Picker("", selection: Binding(
                            get: { model.action(for: row.id) },
                            set: { model.setAction($0, for: row.id) })) {
                            ForEach(ShortcutActions.all) { option in
                                Text(option.title).tag(option.id)
                            }
                        }
                        .labelsHidden()
                        .frame(maxWidth: 320)
                        .disabled(!model.canEdit)
                    } else {
                        Text("Cihazın kendi işlevi").foregroundStyle(.secondary)
                    }
                }
            }
        }
        .padding(6)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var logView: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 2) {
                    ForEach(model.log) { entry in
                        Text(entry.text)
                            .font(.system(.caption, design: .monospaced))
                            .foregroundStyle(.secondary)
                            .textSelection(.enabled)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .id(entry.id)
                    }
                }
                .padding(4)
            }
            .frame(minHeight: 110, maxHeight: .infinity)
            .onChange(of: model.log.count) { _ in
                if let last = model.log.last { proxy.scrollTo(last.id, anchor: .bottom) }
            }
        }
    }
}

struct MenuContent: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Text(model.active ? "Atamalar aktif" : "Hazır")
        Divider()
        Button("Pencereyi aç") {
            openWindow(id: "main")
            NSApp.activate(ignoringOtherApps: true)
        }
        Button("Atamaları durdur") { Task { await model.stop() } }
            .disabled(!model.canStop)
        Divider()
        Button("Uygulamadan çık") { NSApp.terminate(nil) }
    }
}

struct MenuLabel: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        Image(systemName: model.active ? "computermouse.fill" : "computermouse")
    }
}
