// =====================================================================
//  BunoTunner  -  controle de ventoinhas / OC / sensores (visual Windows XP)
//  Compila com o csc.exe que ja vem no Windows (C# 5, .NET Framework 4.x)
//  Veja LEIA-ME.txt
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Management;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using Microsoft.Win32;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using LHM = LibreHardwareMonitor.Hardware;
using Th = System.Threading;

[assembly: AssemblyTitle("BunoTunner")]
[assembly: AssemblyDescription("BunoTunner - controle de ventoinhas, overclock e sensores (open source)")]
[assembly: AssemblyCompany("Buno")]
[assembly: AssemblyProduct("BunoTunner")]
[assembly: AssemblyCopyright("Copyright (c) Buno - código aberto: github.com/Buno-o/BunoTunner")]
[assembly: AssemblyVersion("1.44.1.0")]
[assembly: AssemblyFileVersion("1.44.1.0")]
[assembly: AssemblyInformationalVersion("1.44.1")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]
[assembly: System.Runtime.InteropServices.Guid("6f1b7c1e-3d0a-4c55-9a43-2f7b0f9d4e11")]
[assembly: System.Resources.NeutralResourcesLanguage("pt-BR")]

namespace BunoTunner
{
    // ------------------------------------------------------------------
    //  Entrada. As DLLs do LibreHardwareMonitor ficam embutidas no .exe
    // ------------------------------------------------------------------
    // Registro de tempos da abertura (BunoTunner_startup.log) + tela de abertura
    static class Startup
    {
        static Stopwatch sw = Stopwatch.StartNew();
        static long last;
        static StringBuilder log = new StringBuilder();
        static object lk = new object();
        public static string Status = "Iniciando...";
        public static void Mark(string what)
        {
            lock (lk)
            {
                long now = sw.ElapsedMilliseconds;
                log.AppendLine(now.ToString().PadLeft(6) + " ms  (+" + (now - last).ToString().PadLeft(5) + ")  " + what);
                last = now;
                Status = what;
            }
        }
        public static double Seconds { get { return sw.ElapsedMilliseconds / 1000.0; } }
        public static void Save()
        {
            try
            {
                lock (lk)
                    System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BunoTunner_startup.log"),
                        "BunoTunner - tempos da abertura (cada linha: tempo total, tempo desta etapa)\r\n" + log.ToString());
            }
            catch (Exception) { }
        }

        static Th.Thread st; static SplashForm sp;
        public static void ShowSplash()
        {
            try
            {
                Th.ManualResetEvent ready = new Th.ManualResetEvent(false);
                st = new Th.Thread(delegate ()
                {
                    sp = new SplashForm();
                    sp.Shown += delegate { ready.Set(); };
                    try { Application.Run(sp); } catch (Exception) { ready.Set(); }
                });
                st.SetApartmentState(Th.ApartmentState.STA);
                st.IsBackground = true; st.Start();
                ready.WaitOne(3000);
            }
            catch (Exception) { }
        }
        public static void CloseSplash()
        {
            try { SplashForm f = sp; if (f != null && f.IsHandleCreated) f.BeginInvoke(new Action(f.Close)); }
            catch (Exception) { }
        }
    }

    class SplashForm : Form
    {
        Timer t = new Timer(); int tick;
        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false; TopMost = true; Size = new Size(380, 120);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            t.Interval = 90; t.Tick += delegate { tick++; Invalidate(); }; t.Start();
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { t.Stop(); t.Dispose(); base.OnFormClosed(e); Application.ExitThread(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Xp.Face);
            using (Pen p = new Pen(Xp.CapFrame, 2)) g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            using (Font f = new Font(Xp.Ui.FontFamily, 15f, FontStyle.Bold))
                TextRenderer.DrawText(g, "BunoTunner", f, new Point(18, 14), Xp.Text);
            TextRenderer.DrawText(g, Lang.T("Carregando sensores e hardware..."), Xp.Ui, new Point(20, 50), Xp.Text);
            string st = Lang.T(Startup.Status ?? "");
            if (st.Length > 52) st = st.Substring(0, 52);
            TextRenderer.DrawText(g, st, Xp.Ui, new Point(20, 68), Xp.Dim);
            int x0 = 20, w = Width - 40, bw = 70, pos = (tick * 9) % (w + bw) - bw;
            using (SolidBrush bg = new SolidBrush(Xp.ProgBg)) g.FillRectangle(bg, x0, 94, w, 10);
            g.SetClip(new Rectangle(x0, 94, w, 10));
            using (SolidBrush fg = new SolidBrush(Xp.Prog2)) g.FillRectangle(fg, x0 + pos, 94, bw, 10);
        }
    }

    // ------------------------------------------------------------------
    //  Idiomas: padrao = idioma do Windows (portugues ou ingles); o texto original do programa e em portugues.
    //  A traducao e feita por frases/trechos (do maior para o menor), so na hora de mostrar:
    //  rotulos, botoes, titulos de janela, dicas, menus, listas, caixas de mensagem e textos que mudam durante o uso.
    // ------------------------------------------------------------------
    class LTip : ToolTip
    {
        readonly Dictionary<Control, string> src = new Dictionary<Control, string>();
        public LTip() { Lang.Tips.Add(this); }
        public new void SetToolTip(Control c, string s)
        {
            if (c == null) return;
            if (!src.ContainsKey(c)) { Control cc = c; c.Disposed += delegate { src.Remove(cc); }; }
            src[c] = s;
            base.SetToolTip(c, Lang.T(s));
        }
        public void Retranslate()
        {
            foreach (KeyValuePair<Control, string> kv in new List<KeyValuePair<Control, string>>(src)) base.SetToolTip(kv.Key, Lang.T(kv.Value));
        }
    }

    // MessageBox com o texto/titulo traduzidos
    static class Dlg
    {
        public static DialogResult Show(string text) { return MessageBox.Show(Lang.T(text)); }
        public static DialogResult Show(string text, string caption) { return MessageBox.Show(Lang.T(text), Lang.T(caption)); }
        public static DialogResult Show(string text, string caption, MessageBoxButtons b) { return MessageBox.Show(Lang.T(text), Lang.T(caption), b); }
        public static DialogResult Show(string text, string caption, MessageBoxButtons b, MessageBoxIcon i) { return MessageBox.Show(Lang.T(text), Lang.T(caption), b, i); }
        public static DialogResult Show(IWin32Window o, string text) { return MessageBox.Show(o, Lang.T(text)); }
        public static DialogResult Show(IWin32Window o, string text, string caption) { return MessageBox.Show(o, Lang.T(text), Lang.T(caption)); }
        public static DialogResult Show(IWin32Window o, string text, string caption, MessageBoxButtons b) { return MessageBox.Show(o, Lang.T(text), Lang.T(caption), b); }
        public static DialogResult Show(IWin32Window o, string text, string caption, MessageBoxButtons b, MessageBoxIcon i) { return MessageBox.Show(o, Lang.T(text), Lang.T(caption), b, i); }
    }

    static class Lang
    {
        public static int Cur;   // 0 = portugues, 1 = ingles
        public static List<LTip> Tips = new List<LTip>();
        public static event Action Changed;
        static Dictionary<string, string> en = new Dictionary<string, string>();
        static Dictionary<char, string[]> byFirst = new Dictionary<char, string[]>();
        static Dictionary<string, string> cache = new Dictionary<string, string>();
        static List<WeakReference> menus = new List<WeakReference>();
        sealed class Holder { public string Src; public bool Hooked; }
        static ConditionalWeakTable<object, Holder> hold = new ConditionalWeakTable<object, Holder>();
        static bool busy;
        static Timer sweeper;
        static readonly string[] Pairs = {
            "Placa de video:", "Graphics card:",
            "Tema:", "Theme:",
            "Ventoinhas\nAutomático, manual (% e RPM) e curva de temperatura", "Fans\nAutomatic, manual (% and RPM) and temperature curve",
            "Overclock\nCore clock, memory clock, TDP, limite de temperatura e travar clocks", "Overclock\nCore clock, memory clock, TDP, temperature limit and clock locking",
            "Especificações da GPU\nTodos os dados da placa de video", "GPU specifications\nAll graphics card data",
            "Especificações da CPU\nProcessador, núcleos, cache, clocks e conjuntos de instruções", "CPU specifications\nProcessor, cores, cache, clocks and instruction sets",
            "Benchmark / Estresse\nGPU, CPU, FPU, RAM e SSD com verificação de erros e limite de temperatura", "Benchmark / Stress test\nGPU, CPU, FPU, RAM and SSD with error checking and temperature limit",
            "Memória RAM\nVelocidade, canais, voltagem, módulos e todos os timings que o controlador expuser (tCAS, tRCD, tRP, tRAS, tRFC, Command Rate, tRRD, tFAW, tREFI...)", "RAM\nSpeed, channels, voltage, modules and basic timings (tCAS, tRCD, tRP, tRAS, tRC, tRFC, Command Rate)",
            "Saúde do armazenamento\nVida útil restante, temperatura e S.M.A.R.T. de SSDs NVMe/SATA e HDs", "Storage health\nRemaining life, temperature and S.M.A.R.T. of NVMe/SATA SSDs and HDDs",
            "Internet\nTeste de velocidade (ping, download, upload) e informações da rede", "Internet\nSpeed test (ping, download, upload) and network information",
            "Afinidade de processos\nEscolha em quais núcleos cada programa roda; presets e regras automáticas", "Process affinity\nChoose which cores each program runs on; presets and automatic rules",
            "Desinstalador\nRemove programas por completo: pastas, registro, inicialização, serviços e tarefas que sobram; também remove o que se recusa a desinstalar", "Uninstaller\nRemoves programs completely: leftover folders, registry, startup entries, services and tasks; also removes what refuses to uninstall",
            "Otimizações do Windows\nLimpeza de temporários, compressão de memória e outros ajustes", "Windows optimizations\nTemp cleanup, memory compression and other tweaks",
            "Sensores\nTudo em uma janela: GPU, CPU, placa-mae, memória, discos e ventoinhas, com gráfico, busca e alertas", "Sensors\nEverything in one window: GPU, CPU, motherboard, memory, disks and fans, with graph, search and alerts",
            "Limpar arquivos temporários", "Clean temporary files",
            "Apaga C:\\Windows\\Temp, Prefetch e a pasta %temp% do usuário (itens em uso são ignorados). Nenhum documento pessoal é tocado.", "Deletes C:\\Windows\\Temp, Prefetch and the user %temp% folder (files in use are skipped). No personal documents are touched.",
            "Desativar compressão de memória", "Disable memory compression",
            "Executa Disable-MMAgent -mc: o Windows deixa de comprimir a RAM (usa um pouco menos de CPU, um pouco mais de RAM). Vale após reiniciar o PC.", "Runs Disable-MMAgent -mc: Windows stops compressing RAM (slightly less CPU, slightly more RAM used). Takes effect after a reboot.",
            "Menu de clique direito do Windows 10", "Windows 10 right-click menu",
            "No Windows 11, traz de volta o menu de clique direito completo e clássico do Windows 10 (sem o \"Mostrar mais opções\"). Vale ao reiniciar o Explorer ou fazer logoff.", "On Windows 11, brings back the full classic Windows 10 right-click menu (no \"Show more options\"). Takes effect after restarting Explorer or signing out.",
            "Desativar MPO (Multiplane Overlay)", "Disable MPO (Multiplane Overlay)",
            "Define OverlayTestMode=5 no DWM. Ajuda a resolver tremidas, telas piscando e telas pretas em alguns monitores/placas NVIDIA e AMD. Reinicie o PC para valer.", "Sets OverlayTestMode=5 in DWM. Helps with stutter, flickering and black screens on some monitors/NVIDIA and AMD cards. Reboot to apply.",
            "Overlay DLSS (indicador na tela)", "DLSS overlay (on-screen indicator)",
            "Mostra no canto do jogo um indicador quando o DLSS está em uso (só placas NVIDIA RTX). Aplica na próxima vez que abrir o jogo.", "Shows an indicator in the game corner when DLSS is in use (NVIDIA RTX only). Applies next time the game starts.",
            "Isolamento de núcleo (Integridade de memória)", "Core isolation (Memory integrity)",
            "Recurso de segurança do Windows (HVCI) que isola o núcleo do sistema usando virtualização. Desativar pode dar mais FPS em jogos e liberar drivers antigos, mas reduz a proteção. Reinicie o PC.", "Windows security feature (HVCI) that isolates the system core using virtualization. Disabling may give more FPS in games and allow old drivers, but lowers protection. Reboot required.",
            "Hibernação", "Hibernation",
            "Liga ou desliga a hibernação (powercfg /hibernate). Desligada, apaga o hiberfil.dat (libera vários GB no disco) e também desativa a Inicialização Rápida.", "Turns hibernation on or off (powercfg /hibernate). When off, deletes hiberfil.sys (frees several GB) and also disables Fast Startup.",
            "Aceleração do mouse (Aprimorar precisão do ponteiro)", "Mouse acceleration (Enhance pointer precision)",
            "Desligada, o ponteiro anda sempre a mesma distância para o mesmo movimento da mão (recomendado para jogos). Aplica na hora, sem reiniciar.", "When off, the pointer always moves the same distance for the same hand movement (recommended for games). Applies instantly, no reboot.",
            "Mitigações do Windows (Spectre / Meltdown)", "Windows mitigations (Spectre / Meltdown)",
            "Desativa as mitigações de execução especulativa (FeatureSettingsOverride=3) para ganhar desempenho. REDUZ A SEGURANÇA do PC; use só em máquina de jogos sem dados sensíveis. Reinicie o PC.", "Disables speculative-execution mitigations (FeatureSettingsOverride=3) for performance. LOWERS PC SECURITY; use only on a gaming machine without sensitive data. Reboot required.",
            "Limpar", "Clean",
            "Desativar", "Disable",
            "Ativar", "Enable",
            "Reativar", "Re-enable",
            "Desfazer", "Undo",
            "Padrão do Windows", "Windows default",
            "Desativar MPO", "Disable MPO",
            "Executando...", "Running...",
            "GPU  -  render 3D (estilo FurMark)", "GPU  -  3D render (FurMark style)",
            "Janela OpenGL com bola de pelos e shader; mostra o FPS. Fechar a janela para o teste.", "Opens an OpenGL window with shaders and fur and shows FPS. Closing the window stops the test.",
            "CPU  -  ray tracing 3D (estilo Cinebench)", "CPU  -  3D ray tracing (Cinebench style)",
            "Renderiza uma cena em todos os núcleos, bloco a bloco. Pontos = Mrays/s x 100.", "Renders a scene on all cores, block by block. Points = Mrays/s x 100.",
            "FPU  -  campo de ondas em ponto flutuante", "FPU  -  floating-point wave field",
            "Cálculo de ondas com seno/cosseno em todos os núcleos; mostra GFLOPS estimado.", "Wave calculation with sine/cosine on all cores; shows estimated GFLOPS.",
            "RAM  -  escrita e leitura com verificação", "RAM  -  write and read with verification",
            "Grava e le padrões na memória e confere cada valor; qualquer diferença conta como erro.", "Writes and reads patterns in memory and checks every value; any difference counts as an error.",
            "SSD  -  sequencial + 4K aleatório", "SSD  -  sequential + random 4K",
            "Usa um arquivo temporário no drive escolhido (apagado ao fim). Em 'Até parar' gera desgaste.", "Uses a temporary file on the chosen drive (deleted at the end). 'Until stopped' causes wear.",
            "Duração:", "Duration:",
            "Pelos:", "Fur strands:", "50 mil", "50 thousand", "100 mil", "100 thousand", "250 mil", "250 thousand", "500 mil", "500 thousand", "1 milhão", "1 million",
            "Quantidade de pelos do render. Mais pelos = teste mais pesado (FPS menor).", "Number of fur strands in the render. More strands = heavier test (lower FPS).",
            "Threads:", "Threads:",
            "Usar (% da livre):", "Use (% of free):",
            "Drive:", "Drive:",
            "Tamanho:", "Size:",
            "Parar se passar de", "Stop if above",
            "Rodar sequência (CPU, FPU, RAM, GPU)", "Run sequence (CPU, FPU, RAM, GPU)",
            "Parar tudo", "Stop all",
            "Clique no ícone de um teste para iniciar.", "Click a test icon to start.",
            "Resultado ao vivo", "Live result",
            "Taxa atual", "Current rate",
            "Teste", "Test",
            "Dur.", "Dur.",
            "Media", "Average",
            "Pico", "Peak",
            "Erros", "Errors",
            "T.max", "Max T",
            "Estab.", "Stab.",
            "vs ant.", "vs prev.",
            "Copiar", "Copy",
            "Salvar CSV", "Save CSV",
            "1. Escolha o programa que quer remover", "1. Choose the program to remove",
            "2. Restos encontrados (revise e remova)", "2. Leftovers found (review and remove)",
            "Buscar:", "Search:",
            "Atualizar lista", "Refresh list",
            "Escolher pasta ou .exe...", "Choose folder or .exe...",
            "Desinstalar e limpar restos", "Uninstall and clean leftovers",
            "Remoção forçada", "Forced removal",
            "So procurar restos", "Only scan for leftovers",
            "Encerrar processos", "End processes",
            "Duplo clique desinstala.", "Double-click to uninstall.",
            "Programa", "Program",
            "Editora", "Publisher",
            "Versão", "Version",
            "Tamanho", "Size",
            "Instalado", "Installed",
            "Tipo", "Type",
            "Local", "Location",
            "Certeza", "Certainty",
            "Marcar tudo", "Check all",
            "Desmarcar tudo", "Uncheck all",
            "Remover itens marcados", "Remove checked items",
            "Pastas e arquivos vão para a Lixeira (podem ser recuperados)", "Folders and files go to the Recycle Bin (recoverable)",
            "Salvar cópia (.reg) das chaves do registro antes de apagar", "Save a copy (.reg) of registry keys before deleting",
            "Criar ponto de restauração do Windows antes", "Create a Windows restore point first",
            "Dica: itens com 'Certeza: alta' são ligados ao programa; os de certeza 'media' vêm desmarcados - confira antes.", "Tip: items with 'high' certainty belong to the program; 'medium' ones come unchecked - review them first.",
            "Pasta", "Folder",
            "Exe", "Exe",
            "Cancelar", "Cancel",
            "Cancelando...", "Cancelling...",
            "Teste de velocidade da internet", "Internet speed test",
            "Informações da rede", "Network information",
            "Iniciar teste", "Start test",
            "Mostrar informações da rede", "Show network information",
            "Ocultar", "Hide",
            "Mede ping, download e upload de verdade (servidor da Cloudflare).", "Measures real ping, download and upload (Cloudflare server).",
            "Por privacidade, nada é mostrado até você clicar. Ao clicar aparecem dados pessoais (IPs, MAC, provedor, cidade, nome do Wi-Fi).", "For privacy, nothing is shown until you click. Clicking reveals personal data (IPs, MAC, ISP, city, Wi-Fi name).",
            "Seus discos", "Your disks",
            "Detalhes do disco selecionado", "Selected disk details",
            "Saúde geral:", "Overall health:",
            "Vida útil restante:", "Remaining life:",
            "Temperatura:", "Temperature:",
            "Selecione um disco na lista acima.", "Select a disk in the list above.",
            "Atualizar", "Refresh",
            "Disco", "Disk",
            "Saúde", "Health",
            "Vida", "Life",
            "Temp.", "Temp.",
            "Horas", "Hours",
            "Atributo / sensor", "Attribute / sensor",
            "Valor", "Value",
            "Resumo da memória", "Memory summary",
            "Timings atuais (leitura automática)", "Current timings (automatic reading)",
            "Lendo os timings...", "Reading timings...",
            "Ler novamente", "Read again",
            "Diagnóstico", "Diagnostics",
            "Indicar CPU-Z...", "Set CPU-Z...",
            "Baixar CPU-Z", "Download CPU-Z",
            "Latência da memória (ns)", "Memory latency (ns)",
            "Medir latência", "Measure latency",
            "Parar", "Stop",
            "Latência da RAM (bloco de 256 MB)", "RAM latency (256 MB block)",
            "Afinidade (CPUs)", "Affinity (CPUs)",
            "Prioridade", "Priority",
            "Prioridade:", "Priority:",
            "Processo", "Process",
            "Memória", "Memory",
            "Filtrar:", "Filter:",
            "Marcar mesmo nome", "Check same name",
            "Desmarcar", "Uncheck",
            "Pausar atualização", "Pause updating",
            "Aplicar", "Apply",
            "Aplicar aos marcados", "Apply to checked",
            "Aplicar regras automaticamente", "Apply rules automatically",
            "Regras automáticas", "Automatic rules",
            "Presets", "Presets",
            "Preset", "Preset",
            "Salvar atual", "Save current",
            "Excluir", "Delete",
            "Nova regra", "New rule",
            "Remover", "Remove",
            "Especificações da CPU", "CPU specifications",
            "Especificações da GPU selecionada", "Selected GPU specifications",
            "Propriedade", "Property",
            "Fechar", "Close",
            "Todos", "All",
            "Nenhum", "None",
            "Inverter", "Invert",
            "Físicos", "Physical",
            "Essenciais", "Essentials",
            "1a metade", "1st half",
            "2a metade", "2nd half",
            "Ventoinhas detectadas (Ctrl/Shift = várias)", "Detected fans (Ctrl/Shift = several)",
            "Modo de funcionamento das selecionadas", "Operating mode of the selected fans",
            "Aplicar", "Apply",
            "Aplicar nas selecionadas", "Apply to selected",
            "Tudo automático", "All automatic",
            "Identificar", "Identify",
            "Voltar tudo ao automático ao fechar", "Return everything to automatic on close",
            "Ventoinha", "Fan",
            "Modo", "Mode",
            "Fabricante", "Manufacturer",
            "Buscar (Ctrl+F):", "Search (Ctrl+F):",
            "Janela:", "Window:",
            "Zerar min/max", "Reset min/max",
            "Copiar tudo", "Copy all",
            "Exportar...", "Export...",
            "Sensor", "Sensor",
            "Min", "Min",
            "Max", "Max",
            "Origem", "Source",
            "Redefinir tudo", "Reset all",
            "Restaurar padrão", "Restore default",
            "Destravar tudo", "Unlock all",
            "Travar", "Lock",
            "Overclock / limites", "Overclock / limits",
            "Manual - velocidade fixa em %:", "Manual - fixed speed in %:",
            "Definir por RPM", "Set by RPM",
            "RPM alvo:", "Target RPM:",
            "Automático - a placa/BIOS decide (recomendado no dia a dia)", "Automatic - the card/BIOS decides (recommended for daily use)",
            "Travar clock do núcleo (MHz):  min", "Lock core clock (MHz):  min",
            "Travar clock da memória (MHz):  min", "Lock memory clock (MHz):  min",
            "Atualizar a cada:", "Update every:" };
        static readonly string[] Pairs2 = {
            "Temp. GPU: ", "GPU Temp: ", "Mín: ", "Min: ", "Máx: ", "Max: ",
            "Drivers de GPU e Chipset direto da fabricante", "GPU and Chipset drivers straight from the manufacturer",
            "Consulta o site da NVIDIA / AMD e compara com o driver instalado. Se houver versão mais nova, baixa o instalador oficial, confere a assinatura digital e abre a instalação normal para você concluir. Para Intel, abre o Assistente de Drivers da Intel.", "Checks the NVIDIA / AMD website and compares with the installed driver. If a newer version exists, it downloads the official installer, checks its digital signature and opens the normal installation for you to finish. For Intel, it opens Intel's Driver & Support Assistant.",
            "Verificar GPU e Chipset", "Check GPU and Chipset",
            "Consultando os sites das fabricantes...", "Checking the manufacturers' websites...",
            "Atualizar drivers (todos os dispositivos, GPU e Chipset)", "Update drivers (all devices, GPU and Chipset)", "Windows Update (todos os dispositivos)", "Windows Update (all devices)", "Site da fabricante (GPU NVIDIA/AMD e chipset AMD)", "Manufacturer website (NVIDIA/AMD GPU and AMD chipset)",
            "Marque pelo menos uma das origens (Windows Update ou site da fabricante).", "Tick at least one source (Windows Update or manufacturer website).", "---- Windows Update ----", "---- Windows Update ----", "---- Site da fabricante ----", "---- Manufacturer website ----",
            "Concluído.", "Done.", "Ignorado.", "Skipped.", "gravando", "writing", "conferindo", "verifying", "conferido sem erro", "verified, no error", "erro", "error", "Padrão", "Pattern", "Passagem", "Pass", "Blocos", "Blocks", "Reservando a memória do teste...", "Reserving the test memory...", "Escrita sequencial (1 MB)", "Sequential write (1 MB)", "Leitura sequencial + conferência", "Sequential read + verification", "Leitura aleatória 4K", "Random 4K read", "Histórico de testes", "Test history", "Data e hora", "Date and time", "Avaliação da conexão", "Connection rating", "Faça um teste para ver se a sua conexão serve para jogos, streaming e chamadas.", "Run a test to see whether your connection suits gaming, streaming and calls.", "Medindo...", "Measuring...", "Copiar linha selecionada", "Copy selected row", "Controle, segurança e resultado ao vivo", "Controls, safety and live results", "Taxa atual", "Current rate", "Copiar relatório", "Copy report", "Salvar relatório...", "Save report...", "Mostrar só alertas", "Show alerts only", "Filtrar:", "Filter:", "Situação", "Status", "Selecione um disco primeiro.", "Select a disk first.", "Relatório copiado para a área de transferência.", "Report copied to the clipboard.", "Relatório salvo.", "Report saved.", "Inverter seleção", "Invert selection", "Dica: Enter nos campos de % ou RPM também aplica.", "Tip: Enter in the % or RPM boxes also applies.", "Identificar = liga as selecionadas a 100% por 5 s para você descobrir qual é qual.  Botão direito na lista: selecionar por tipo ou renomear (inclua CPU, Bomba ou Gabinete no nome). Atalhos: Ctrl+A seleciona tudo, F2 ou duplo clique renomeia.", "Identify = runs the selected fans at 100% for 5 s so you can tell which is which.  Right-click the list: select by type or rename (include CPU, Pump or Case in the name). Shortcuts: Ctrl+A selects all, F2 or double-click renames.", "Limpeza e manutenção", "Cleanup and maintenance", "Libera espaço no disco.", "Frees disk space.", "Desempenho em jogos", "Gaming performance", "Ajustes que podem melhorar FPS, tremidas e resposta do mouse.", "Tweaks that can improve FPS, stutter and mouse response.", "Interface do Windows", "Windows interface", "Aparência e comportamento do sistema.", "System look and behavior.", "Segurança (avançado)", "Security (advanced)", "Estes ajustes REDUZEM a proteção do PC. Use só se souber o que está fazendo.", "These tweaks REDUCE the PC's protection. Use only if you know what you are doing.", "Mantenha a GPU, o chipset e os demais dispositivos atualizados.", "Keep the GPU, chipset and other devices up to date.",
            "Instalador fechado.", "Installer closed.",
            "Abrindo o instalador. Conclua a instalação na janela da fabricante...", "Opening the installer. Finish the installation in the manufacturer's window...",
            "Instalador apagado por segurança. Nada foi executado.", "Installer deleted for safety. Nothing was run.",
            "Nenhuma placa de video NVIDIA/AMD ou chipset AMD identificado.", "No NVIDIA/AMD graphics card or AMD chipset identified.",
            "Intel: a Intel não oferece consulta pública confiável de versões; o Assistente de Drivers e Suporte da Intel faz isso.", "Intel: Intel offers no reliable public version lookup; Intel's Driver & Support Assistant does that.",
            "Desfazer", "Undo", "Refazer", "Redo", "Limpar lista", "Clear list", "Restaurar padrão", "Restore defaults", "Exportar...", "Export...", "Importar...", "Import...",
            "Todos os grupos", "All groups", "Adicionar ao overlay", "Add to overlay", "Adicionar todos os resultados da busca", "Add all search results",
            "Aplicar esta cor a todos os itens", "Apply this color to all items",
            "Configuração do overlay (*.bto)|*.bto|Todos os arquivos|*.*", "Overlay settings (*.bto)|*.bto|All files|*.*",
            "Exportar configuração do overlay", "Export overlay settings", "Importar configuração do overlay", "Import overlay settings",
            "Configuração exportada.", "Settings exported.", "Configuração importada. Use Ctrl+Z para voltar a anterior.", "Settings imported. Press Ctrl+Z to go back to the previous ones.",
            "Esse arquivo não tem uma configuração valida do overlay.", "This file does not contain a valid overlay configuration.",
            "Defina antes uma cor para o item selecionado.", "First set a color for the selected item.",
            "Restaurar todas as opções do overlay (itens, cores, nomes, tamanho, opacidade...) para o padrão?\nVocê pode desfazer com Ctrl+Z.", "Restore all overlay options (items, colors, names, size, opacity...) to the defaults?\nYou can undo with Ctrl+Z.",
            "Cor...", "Color...", "Renomear...", "Rename...", "Renomear", "Rename", "Subir", "Move up", "Descer", "Move down", "Resetar", "Reset",
            "Resetar cor e nome", "Reset color and name", "Remover do overlay", "Remove from overlay",
            "Nome que aparece no overlay (deixe vazio para voltar ao nome original):", "Name shown in the overlay (leave empty to restore the original):",
            "Selecione antes um item na lista da direita (Ordem no overlay).", "First select an item in the right-hand list (Order in overlay).",
                        "Compacto (só os valores, sem nomes)", "Compact (values only, no names)", "Aplicar a cor também ao nome", "Also apply the color to the name",
            "Temperatura em vermelho a partir de:", "Red temperature from:", "Desligado", "Off",
            "Ativado", "Enabled", "Desativado", "Disabled",
            "Interface física (PHY)", "Physical interface (PHY)",
            "Velocidade (MT/s):", "Speed (MT/s):", "Gear Down Mode:", "Gear Down Mode:", "Burst length:", "Burst length:",
            "Principais", "Main",
            "Ativação e escrita", "Activation and write",
            "Refresh e calibração", "Refresh and calibration",
            "Leitura / escrita entre bancos e ranks", "Read / write between banks and ranks",
            "Economia de energia", "Power saving",
            "Treino do controlador (RTL / IOL)", "Controller training (RTL / IOL)",
            "RTL canal A / DIMM0:", "RTL channel A / DIMM0:", "RTL canal A / DIMM1:", "RTL channel A / DIMM1:",
            "RTL canal B / DIMM0:", "RTL channel B / DIMM0:", "RTL canal B / DIMM1:", "RTL channel B / DIMM1:",
            "IOL canal A / DIMM0:", "IOL channel A / DIMM0:", "IOL canal A / DIMM1:", "IOL channel A / DIMM1:",
            "IOL canal B / DIMM0:", "IOL channel B / DIMM0:", "IOL canal B / DIMM1:", "IOL channel B / DIMM1:",
            "Overlay em jogos...", "In-game overlay...",
            "Overlay em jogos", "In-game overlay",
            "Ativar overlay em jogos", "Enable in-game overlay",
            "Tamanho:", "Size:",
            "Opacidade:", "Opacity:", "Layout:", "Layout:", "Padrão (fundo + contorno)", "Default (background + border)", "Sem fundo nem contorno", "No background or border",
            "So contorno (sem fundo)", "Border only (no background)", "Barra lateral", "Side bar", "Neon", "Neon", "Terminal (verde)", "Terminal (green)", "Tema: Luna (XP)", "Theme: Luna (XP)", "Tema: Frutiger Aero (Win 7)", "Theme: Frutiger Aero (Win 7)", "Tema: Dark / AMOLED", "Theme: Dark / AMOLED", "Tema: Neon / Cyberpunk", "Theme: Neon / Cyberpunk", "Tema: Steampunk", "Theme: Steampunk", "Tema: Vintage (Win 95)", "Theme: Vintage (Win 95)", "Tema: GTA VI (Vice City)", "Theme: GTA VI (Vice City)", "Vidro (translúcido)", "Glass (translucent)", "Pílula", "Pill", "Claro minimalista", "Light minimal", "Barra superior", "Top bar", "Barra inferior", "Bottom bar", "Âmbar (monitor antigo)", "Amber (old monitor)", "HUD ciano", "Cyan HUD", "Oceano", "Ocean", "Floresta", "Forest", "Rosa", "Pink", "Papel", "Paper", "Faixa escura (sem borda)", "Dark strip (no border)", 
            "Mostrar:", "Show:",
            "Somente em jogos (tela cheia ou janela sem borda)", "Only in games (fullscreen or borderless)",
            "Em qualquer programa que desenhe com DirectX", "In any program that renders with DirectX",
            "Sempre (para testar)", "Always (for testing)",
            "Sensores disponíveis  (duplo clique ou arraste para a direita)", "Available sensors  (double-click or drag to the right)",
            "Ordem no overlay  (arraste para reordenar)", "Order in the overlay  (drag to reorder)",
            "Posição:", "Position:", "Atalho:", "Hotkey:", "Mostrar/ocultar: ", "Show/hide: ", "(nenhum)", "(none)", "Pressione o atalho...  (Esc cancela, Del limpa)", "Press the hotkey...  (Esc cancels, Del clears)",
            "Sup. esq.", "Top left", "Sup. dir.", "Top right", "Inf. esq.", "Bottom left", "Inf. dir.", "Bottom right",
            "Editar na tela", "Edit on screen", "Concluir edição", "Finish editing", "Concluir", "Done",
            "So FPS", "FPS only",
            "Arraste aqui para mover", "Drag here to move",
            "(vazio - adicione sensores no menu)", "(empty - add sensors in the menu)",
            "FPS (API + quadros/s)", "FPS (API + frames/s)", "Jogo", "Game",
            "Dica: em \"Editar na tela\" o overlay aparece no desktop - arraste pelo topo para mover, arraste as linhas para reordenar e use o botão direito numa linha para remover. O FPS mostra a API gráfica do jogo e os quadros por segundo. Jogos em tela cheia exclusiva (não a sem borda) podem esconder o overlay. O atalho Mostrar/ocultar e global e fica reservado para o BunoTunner.", "Tip: with \"Edit on screen\" the overlay appears on the desktop - drag the top to move it, drag the rows to reorder and right-click a row to remove it. FPS shows the game's graphics API and frames per second. Exclusive-fullscreen games (not borderless) may hide the overlay. The Show/hide hotkey is global and reserved for BunoTunner.",
            "Procurando atualizações...", "Looking for updates...",
            "Baixando atualização...", "Downloading update...",
            "Verificar atualizações\nProcura uma versão mais nova do BunoTunner e, se você quiser, baixa e instala.", "Check for updates\nLooks for a newer BunoTunner version and, if you want, downloads and installs it.",
            "Não foi possível verificar atualizações:\n", "Could not check for updates:\n",
            "A versão mais recente não foi encontrada.", "The latest version was not found.",
            "Você já está na versão mais recente (", "You already have the latest version (",
            "Nova versão disponível: ", "New version available: ",
            " (atual: ", " (current: ",
            "Deseja baixar e atualizar agora?", "Do you want to download and update now?",
            "Falha ao baixar a atualização:\n", "Failed to download the update:\n",
            "O arquivo baixado e inválido.", "The downloaded file is invalid.",
            "O SHA-256 do arquivo baixado não confere com o publicado no GitHub; o arquivo foi descartado.", "The downloaded file's SHA-256 does not match the one published on GitHub; the file was discarded.",
            "A nova versão foi baixada, mas não foi possível substituir o arquivo atual.\nEla foi salva em:\n", "The new version was downloaded, but the current file could not be replaced.\nIt was saved at:\n",
            "Atualizado para ", "Updated to ",
            " (SHA-256 conferido).", " (SHA-256 verified).",
            "Reiniciar o BunoTunner agora?", "Restart BunoTunner now?",
            "Iniciar com o Windows: LIGADO\nClique para desligar.", "Start with Windows: ON\nClick to turn off.",
            "Iniciar com o Windows: desligado\nClique para ligar (abre na bandeja ao entrar no Windows).", "Start with Windows: off\nClick to turn on (opens in the tray when you log in to Windows).",
            "Não foi possível alterar a inicialização com o Windows:\n", "Could not change the start with Windows setting:\n",
            "  Busca falhou (", "  Search failed (",
            "Windows Update", "Windows Update",
            "servidor padrão", "default server",
            "Não foi possível consultar o Windows Update: ", "Could not query Windows Update: ",
            "  O serviço Windows Update está desativado; ativando para esta operação...", "  The Windows Update service is disabled; enabling it for this operation...",
            "O serviço Windows Update esta desativado.", "The Windows Update service is disabled.",
            "Sem acesso ao servidor do Windows Update (internet, DNS, proxy ou firewall).", "No access to the Windows Update server (internet, DNS, proxy or firewall).",
            "O serviço Windows Update não está pronto; reinicie o Windows e tente de novo.", "The Windows Update service is not ready; restart Windows and try again.",
            "Acesso negado; execute o programa como administrador.", "Access denied; run the program as administrator.",
            "O servidor do Windows Update esta ocupado; tente em alguns minutos.", "The Windows Update server is busy; try again in a few minutes.",
            "Erro: ", "Error: ",
            "Continuar?", "Continue?",
            "Atualizar drivers de todos os dispositivos", "Update drivers for all devices",
            "Analisa todos os dispositivos do PC e procura no Windows Update os drivers mais novos. Antes de instalar, cria um ponto de restauração e confere a assinatura digital e o hash SHA-256 dos arquivos baixados; se algum estiver alterado, nada é instalado.", "Scans every device in the PC and looks for newer drivers on Windows Update. Before installing, it creates a restore point and checks the digital signature and SHA-256 hash of the downloaded files; if any is altered, nothing is installed.",
            "Atualizar drivers", "Update drivers",
            "Só verificar", "Check only",
            "Clique em 'Atualizar drivers' para analisar e instalar, ou em 'Só verificar' para apenas listar o que está desatualizado.", "Click 'Update drivers' to scan and install, or 'Check only' to just list what is outdated.",
            "1/6  Analisando os dispositivos do sistema...", "1/6  Scanning the system's devices...",
            "2/6  Procurando drivers mais novos no Windows Update (pode levar alguns minutos)...", "2/6  Looking for newer drivers on Windows Update (may take a few minutes)...",
            "3/6  Criando ponto de restauração do sistema...", "3/6  Creating a system restore point...",
            "4/6  Baixando os drivers...", "4/6  Downloading the drivers...",
            "5/6  Verificando assinatura digital e hash SHA-256 dos arquivos baixados...", "5/6  Checking digital signature and SHA-256 hash of the downloaded files...",
            "6/6  Instalando...", "6/6  Installing...",
            "Dispositivos com driver: ", "Devices with a driver: ",
            "   sem assinatura: ", "   unsigned: ",
            "   com problema: ", "   with problems: ",
            "  Sem assinatura: ", "  Unsigned: ",
            "  Com problema (código ", "  Problem (code ",
            "  Ponto de restauração criado.", "  Restore point created.",
            "  Não foi possível criar o ponto de restauração (", "  Could not create the restore point (",
            "). Continuando.", "). Continuing.",
            "Encontrados: ", "Found: ",
            " driver(s) mais novo(s).", " newer driver(s).",
            "Todos os drivers já estão na versão mais nova oferecida pelo Windows Update.", "All drivers are already at the newest version offered by Windows Update.",
            "Verificação concluída: ", "Check finished: ",
            " driver(s) podem ser atualizados.", " driver(s) can be updated.",
            "Cancelado pelo usuário. Nada foi instalado.", "Cancelled by the user. Nothing was installed.",
            "Falha no download (código ", "Download failed (code ",
            "). Nada foi instalado.", "). Nothing was installed.",
            "  Assinaturas validas: ", "  Valid signatures: ",
            "   sem assinatura própria (verificados pelo Windows Update): ", "   without their own signature (verified by Windows Update): ",
            "   alterados/não confiáveis: ", "   altered/untrusted: ",
            "ABORTADO: ", "ABORTED: ",
            " arquivo(s) com hash/assinatura inválidos. Nada foi instalado.", " file(s) with invalid hash/signature. Nothing was installed.",
            "  Instalado: ", "  Installed: ",
            "  FALHOU (código ", "  FAILED (code ",
            "Concluído: ", "Done: ",
            " instalado(s), ", " installed, ",
            " com falha.", " failed.",
            " REINICIE o Windows para terminar.", " RESTART Windows to finish.",
            "Estes drivers serão baixados, verificados (assinatura e hash SHA-256) e instalados:", "These drivers will be downloaded, verified (signature and SHA-256 hash) and installed:",
            "Será criado um ponto de restauração antes. A tela pode piscar (driver de video) e pode ser preciso reiniciar o Windows.", "A restore point will be created first. The screen may flicker (video driver) and Windows may need to restart.",
            "Abrir BunoTunner", "Open BunoTunner",
            "Botão - (minimizar) envia para a bandeja", "The - (minimize) button sends it to the tray",
            "Continua rodando na bandeja. Clique duas vezes no ícone para abrir.", "Still running in the tray. Double-click the icon to open.",
            "Sair", "Exit",
            "Placa:", "Card:",
            "Controle e segurança", "Control and safety",
            "A placa e a escolhida la em cima. Se o OpenGL abrir em outra, o teste muda para OpenCL na escolhida.", "The card is the one chosen at the top. If OpenGL opens on another one, the test switches to OpenCL on the chosen card.",
            "O OpenGL abriu na placa '", "OpenGL opened on card '",
            "', diferente da selecionada ('", "', different from the selected one ('",
            "'): usando o teste OpenCL na placa selecionada.", "'): using the OpenCL test on the selected card.",
            "Erros: ", "Errors: ",
            "FPS do render: ", "Render FPS: ",
            "Pontos: ", "Score: ",
            "Tempo: ", "Time: ",
            " (restam ", " (remaining ",
            "  Velocidade do link: ", "  Link speed: ",
            "  blocos: ", "  blocks: ",
            "  máscara ", "  mask ",
            "  tempo: ", "  time: ",
            "  último: ", "  last: ",
            "  (estimado)", "  (estimated)",
            "  ATENÇÃO: ", "  WARNING: ",
            "  AVISO: overclock pode causar travamentos e artefatos. Vá aos poucos e teste a estabilidade.\n  Vale até reiniciar ou clicar em Redefinir. Funciona em NVIDIA, AMD (Overdrive8) e Intel Arc.", "  WARNING: overclocking can cause crashes and artifacts. Go in small steps and test stability.\n  Lasts until reboot or until you click Reset. Works on NVIDIA, AMD (Overdrive8) and Intel Arc.",
            "  CEP: ", "  ZIP: ",
            "  Pontos CPU: ", "  CPU score: ",
            "  Próximo teste em instantes...", "  Next test in a moment...",
            "  Sequência concluída.", "  Sequence finished.",
            "  [driver le ", "  [driver reads ",
            "  nenhum tipo PawnIO na DLL (DLL antiga: atualize a LibreHardwareMonitorLib.dll)", "  no PawnIO type in the DLL (old DLL: update LibreHardwareMonitorLib.dll)",
            "  recurso ", "  feature ",
            "  tipo ", "  type ",
            "  |  Dica: em notebooks o fabricante (BIOS/vBIOS) pode bloquear o overclock; atualize o driver NVIDIA e tente valores menores. A seção \"Travar clock\" costuma funcionar mesmo assim.", "  |  Tip: on laptops the manufacturer (BIOS/vBIOS) may block overclocking; update the NVIDIA driver and try smaller values. The \"Lock clock\" section usually works anyway.",
            "  |  pelos OpenGL", "  |  OpenGL fur",
            "  |  shader + pelos OpenGL", "  |  shader + OpenGL fur",
            " (BIOS legado / CSM)", " (legacy BIOS / CSM)",
            " (BIOS legado ou sem suporte)", " (legacy BIOS or unsupported)",
            " (Esc fecha e para)", " (Esc closes and stops)",
            " (Polaris/Vega/antiga): ajuste de clocks só no AMD Adrenalin; a ventoinha funciona se o driver permitir", " (Polaris/Vega/older): clock tuning only in AMD Adrenalin; the fan works if the driver allows it",
            " (Windows/driver sem suporte)", " (unsupported Windows/driver)",
            " (agora / máximo)", " (now / max)",
            " (aguardando reinicio)", " (waiting for reboot)",
            " (confira se o ajuste manual esta liberado no AMD Adrenalin)", " (check that manual tuning is enabled in AMD Adrenalin)",
            " (em execução)", " (running)",
            " (erro ", " (error ",
            " (limite ", " (limit ",
            " (sem TPM ou desativado no firmware)", " (no TPM or disabled in firmware)",
            " (sequência cancelada)", " (sequence cancelled)",
            " (só leitura)", " (read-only)",
            " CPUs marcados", " CPUs checked",
            " Copias do registro em BunoTunner_backup.", " Registry copies in BunoTunner_backup.",
            " MB liberados.", " MB freed.",
            " MB/s | 4K aleatório ", " MB/s | random 4K ",
            " MB/s | Leitura seq ", " MB/s | Seq. read ",
            " MHz     Memória: ", " MHz     Memory: ",
            " MHz reais", " real MHz",
            " adaptador(es):", " adapter(s):",
            " ainda não foi configurado.\nEm breve!", " has not been set up yet.\nComing soon!",
            " ajustada(s).", " adjusted.",
            " alerta(s)", " alert(s)",
            " arquivo(s) apagado(s), ", " file(s) deleted, ",
            " arquivo(s) em uso serão apagados no próximo reinicio do Windows.", " file(s) in use will be deleted on the next Windows restart.",
            " canais", " channels",
            " canal(ais)).  ", " channel(s)).  ",
            " canal(is) em ", " channel(s) at ",
            " com falha.", " failed.",
            " dias)", " days)",
            " disco(s). Alguns discos USB/RAID não informam S.M.A.R.T. ao Windows.", " disk(s). Some USB/RAID disks do not report S.M.A.R.T. to Windows.",
            " do sistema ignorado(s)", " system item(s) skipped",
            " durante o cálculo (o driver pode ter reiniciado).", " during the calculation (the driver may have restarted).",
            " e o CPU-Z não foi encontrado", " and CPU-Z was not found",
            " e o CPU-Z não retornou dados", " and CPU-Z returned no data",
            " erro 0x", " error 0x",
            " erro(s) de cálculo/verificação - sistema instável!", " calculation/verification error(s) - unstable system!",
            " falhou (", " failed (",
            " item(ns) encontrado(s). Revise a lista (só os de certeza alta vêm marcados) e clique em 'Remover itens marcados'.", " item(s) found. Review the list (only high-certainty ones come checked) and click 'Remove checked items'.",
            " item(ns) marcado(s)?\n\n", " checked item(s)?\n\n",
            " item(ns) removido(s)", " item(s) removed",
            " módulo(s))", " module(s))",
            " no gráfico  |  ", " on the graph  |  ",
            " núcleos, ", " cores, ",
            " processo(s) alterado(s)", " process(es) changed",
            " processo(s) encerrado(s). Procurando restos...", " process(es) ended. Looking for leftovers...",
            " processo(s) finalizado(s).", " process(es) ended.",
            " programas. Escolha um na lista.", " programs. Choose one from the list.",
            " rank(s) no total", " rank(s) in total",
            " s   melhor: ", " s   best: ",
            " sem RPM máximo medido: use \"Identificar\" antes.", " no measured max RPM: use \"Identify\" first.",
            " sensores  |  ", " sensors  |  ",
            " sensores)", " sensors)",
            " ventoinha(s) aplicada(s). ", " fan(s) applied. ",
            " ventoinha(s) do tipo \"", " fan(s) of type \"",
            "% (a placa pode subir se esquentar)", "% (the card may go higher if it heats up)",
            "% restante", "% remaining",
            "(!) Os sensores da CPU não estão sendo lidos pelo driver (temperatura/tensão/consumo). Clique aqui para ver como resolver.", "(!) CPU sensors are not being read by the driver (temperature/voltage/power). Click here to see how to fix it.",
            "(indisponível)", "(unavailable)",
            "(interrompido)", "(interrupted)",
            "(negado)", "(denied)",
            "(use \"Identificar\" antes: ele mede o RPM máximo de cada ventoinha)", "(use \"Identify\" first: it measures each fan's max RPM)",
            ") não expõe os timings sem um driver próprio", ") does not expose timings without its own driver",
            ").\nAbrir o download no navegador?", ").\nOpen the download in the browser?",
            "): usando o teste OpenCL.", "): using the OpenCL test.",
            ")?\nTrabalho não salvo nele será perdido.", ")?\nUnsaved work in it will be lost.",
            "- Botão direito: criar ALERTA (som), copiar nome/valor, zerar min/max.\n", "- Right-click: create an ALERT (sound), copy name/value, reset min/max.\n",
            "- Cartões no topo: os valores principais (GPU, CPU, RAM, ventoinha) sempre a vista.\n", "- Cards on top: the main values (GPU, CPU, RAM, fan) always in view.\n",
            "- Exportar salva os valores/histórico em CSV ou o gráfico em PNG.", "- Export saves the values/history as CSV or the graph as PNG.",
            "- Gráfico: passe o mouse para ver os valores; \"Normalizar escala\" coloca unidades diferentes lado a lado.\n", "- Graph: hover to see the values; \"Normalize scale\" puts different units side by side.\n",
            "- Lista: escolha a categoria a esquerda (GPU, CPU, Placa-mae, Memória...) e use a busca (Ctrl+F) e o filtro de tipo.\n", "- List: choose the category on the left (GPU, CPU, Motherboard, Memory...) and use search (Ctrl+F) and the type filter.\n",
            "- Marque a caixinha (ou de duplo clique) para ver o sensor no gráfico.\n", "- Tick the checkbox (or double-click) to see the sensor on the graph.\n",
            "- \"Atualizar a cada\" muda a velocidade das leituras (em milissegundos).\n", "- \"Update every\" changes the reading speed (in milliseconds).\n",
            "- \"Essenciais\" marca os sensores mais importantes de uma vez.\n", "- \"Essentials\" ticks the most important sensors at once.\n",
            "--- Adaptador: ", "--- Adapter: ",
            "--- Ao vivo ---", "--- Live ---",
            "--- Overclock / controle (", "--- Overclock / control (",
            "--- Recursos (ligado / desligado) ---", "--- Features (on / off) ---",
            "--- Recursos e segurança (ligado / desligado) ---", "--- Features and security (on / off) ---",
            ". Clique em Diagnóstico e envie o texto para análise.", ". Alternative: download CPU-Z (ZIP version), extract it next to the program or use \"Set CPU-Z\"; the program only uses it to read the timings.",
            ". Esta placa/driver não aceitou controle manual.", ". This card/driver did not accept manual control.",
            ". Use 'Remoção forçada' ou 'So procurar restos'.", ". Use 'Forced removal' or 'Only scan for leftovers'.",
            "2a thread", "2nd thread",
            "1) \"Integridade de memória\" (Isolamento de núcleo) do Windows ou o Defender bloqueando o driver: desative em Segurança do Windows > Segurança do dispositivo > Isolamento de núcleo e reinicie o PC.\n", "1) Windows \"Memory integrity\" (Core isolation) or Defender blocking the driver: turn it off in Windows Security > Device security > Core isolation and restart the PC.\n",
            "2) Versão do LibreHardwareMonitor antiga para a sua CPU: use a mais recente (versões novas podem exigir o driver PawnIO).\n", "2) LibreHardwareMonitor version too old for your CPU: use the latest one (newer versions may require the PawnIO driver).\n",
            "3) Antivírus de terceiros.\n\n", "3) Third-party antivírus.\n\n",
            "; rode como administrador); ", "; run as administrator); ",
            "=== Este computador ===", "=== This computer ===",
            "=== Internet (visto de fora) ===", "=== Internet (seen from outside) ===",
            "?  Como usar", "?  How to use",
            "A placa Intel não expõe controle de ventoinha.", "The Intel card does not expose fan control.",
            "A placa-mae pode não expor as ventoinhas ao Windows", "The motherboard may not expose the fans to Windows",
            "A própria placa-mae ou o driver controla a velocidade pela temperatura.", "The motherboard or driver itself controls the speed based on temperature.",
            "A ventoinha fica sempre na mesma velocidade, não importa a temperatura.", "The fan always stays at the same speed, no matter the temperature.",
            "ADL incompleta", "ADL incomplete",
            "ADL sem Overdrive8", "ADL without Overdrive8",
            "ADL sem Overdrive8_Setting_Set", "ADL without Overdrive8_Setting_Set",
            "ADL2_Main_Control_Create erro ", "ADL2_Main_Control_Create error ",
            "ALERTA: ", "ALERT: ",
            "AMD (plataforma não identificada)", "AMD (platform not identified)",
            "ATENÇÃO: as informações a seguir são PESSOAIS.\n\nVão aparecer seu IP interno e externo, endereços MAC, provedor, cidade aproximada, nome da rede Wi-Fi e DNS.\nNão compartilhe prints desta tela.\n\nMostrar mesmo assim?", "WARNING: the following information is PERSONAL.\n\nYour internal and external IP, MAC addresses, ISP, approximate city, Wi-Fi network name and DNS will appear.\nDo not share screenshots of this screen.\n\nShow it anyway?",
            "ATENÇÃO: desativar as mitigações de Spectre/Meltdown deixa o PC mais vulnerável a ataques que leem memória (por exemplo, por código malicioso no navegador). O ganho de desempenho costuma ser pequeno em CPUs recentes.\n\nDesativar mesmo assim?", "WARNING: disabling Spectre/Meltdown mitigations makes the PC more vulnerable to attacks that read memory (for example, malicious code in the browser). The performance gain is usually small on recent CPUs.\n\nDisable anyway?",
            "ATENÇÃO: o disco indica falha iminente ou pouca vida útil. Faça backup AGORA e planeje a troca.", "WARNING: the disk reports imminent failure or little remaining life. Back up NOW and plan a replacement.",
            "ATENÇÃO: ", "WARNING: ",
            "Abaixo do normal", "Below normal",
            "Aceleração de GPU por hardware (HAGS)", "Hardware-accelerated GPU scheduling (HAGS)",
            "Aceleração do mouse ATIVADA.", "Mouse acceleration ENABLED.",
            "Aceleração do mouse DESATIVADA.", "Mouse acceleration DISABLED.",
            "Acima do normal", "Above normal",
            "AdapterInfo_Get falhou (", "AdapterInfo_Get failed (",
            "Adicionado: ", "Added: ",
            "Administrador: ", "Administrator: ",
            "Alerta definido: ", "Alert set: ",
            "Alerta removido.", "Alert removed.",
            "Alerta", "Alert",
            "Alta", "High",
            "Aplicar também a TODOS os processos com o mesmo nome", "Also apply to ALL processes with the same name",
            "Apoie o BunoTunner via LivePix", "Support BunoTunner via LivePix",
            "Apoie o BunoTunner via PayPal", "Support BunoTunner via PayPal",
            "Armazenamento (HD/SSD)", "Storage (HDD/SSD)",
            "Armazenamento", "Storage",
            "Arquitetura", "Architecture",
            "Arquivo", "File",
            "Atalho", "Shortcut",
            "Até parar", "Until stopped",
            "Atenção", "Attention",
            "Automático", "Automatic",
            "Avisar (som + barra de status) quando \"", "Notify (sound + status bar) when \"",
            "Aviso", "Warning",
            "Baixa (Idle)", "Low (Idle)",
            "Baixar e instalar o PawnIO agora?\n\nO instalador oficial (github.com/namazso/PawnIO.Setup) será baixado e pedirá permissão de administrador.", "Download and install PawnIO now?\n\nThe official installer (github.com/namazso/PawnIO.Setup) will be downloaded and will ask for administrator permission.",
            "Banda teórica: ", "Theoretical bandwidth: ",
            "Barramento da memória", "Memory bus",
            "Bateria", "Battery",
            "Boa", "Good",
            "Bomba/AIO", "Pump/AIO",
            "Boot em modo UEFI (necessário p/ ReBAR)", "UEFI boot mode (required for ReBAR)",
            "Boot em modo UEFI", "UEFI boot mode",
            "BunoTunner - Adicionar manualmente", "BunoTunner - Add manually",
            "BunoTunner - Dados pessoais", "BunoTunner - Personal data",
            "BunoTunner - Desinstalar", "BunoTunner - Uninstall",
            "BunoTunner - Otimizações", "BunoTunner - Optimizations",
            "BunoTunner - PawnIO recomendado", "BunoTunner - PawnIO recommended",
            "BunoTunner - Remoção forçada", "BunoTunner - Forced removal",
            "BunoTunner - Remover", "BunoTunner - Remove",
            "BunoTunner - Render do teste (Esc fecha e para)", "BunoTunner - Test render (Esc closes and stops)",
            "BunoTunner - Sensores (GPU, CPU, placa-mae, memória, discos, ventoinhas...)", "BunoTunner - Sensors (GPU, CPU, motherboard, memory, disks, fans...)",
            "BunoTunner - Sensores", "BunoTunner - Sensors",
            "BunoTunner - diagnóstico dos timings", "BunoTunner - timings diagnostics",
            "BunoTunner - erro fatal", "BunoTunner - fatal error",
            "BunoTunner - tempos da abertura (cada linha: tempo total, tempo desta etapa)\r\n", "BunoTunner - startup timings (each line: total time, time of this step)\r\n",
            "COMO USAR OS SENSORES\n\n", "HOW TO USE THE SENSORS\n\n",
            "CPU - consumo", "CPU - power",
            "CPU - render 3D por blocos (ray tracing)", "CPU - block-based 3D render (ray tracing)",
            "CPU - temperatura", "CPU - temperature",
            "CPU - uso", "CPU - usage",
            "Programas (*.exe)|*.exe|Todos|*.*", "Programs (*.exe)|*.exe|All|*.*",
            "CPU-Z (cpuz*.exe)|cpuz*.exe|Programas|*.exe", "CPU-Z (cpuz*.exe)|cpuz*.exe|Programs|*.exe",
            "Canais:", "Channels:",
            "Cancelado.", "Cancelled.",
            "Cancelado", "Cancelled",
            "Capacidade total:", "Total capacity:",
            "Capacidade", "Capacity",
            "Carregando sensores e hardware...", "Loading sensors and hardware...",
            "Causas comuns:\n", "Common causes:\n",
            "Ciclos liga/desliga", "Power cycles",
            "Cidade: ", "City: ",
            "Clock atual (WMI)", "Current clock (WMI)",
            "Clock base", "Base clock",
            "Clock da memória", "Memory clock",
            "Clock de video", "Video clock",
            "Clock do núcleo", "Core clock",
            "Clock externo (BCLK)", "External clock (BCLK)",
            "Clock máximo", "Max clock",
            "Clock médio dos núcleos", "Average core clock",
            "Clock travado.", "Clock locked.",
            "Clocks atuais e suporte desta placa", "Current clocks and support of this card",
            "Clocks destravados.", "Clocks unlocked.",
            " (agora)", " (now)",
            "Código ", "Code ",
            "Coletando informações...", "Collecting information...",
            "Compressão de memória desativada. Reinicie o Windows para valer.", "Memory compression disabled. Restart Windows for it to take effect.",
            "Compressão de memória reativada. Reinicie o Windows para valer.", "Memory compression re-enabled. Restart Windows for it to take effect.",
            "Concluído: ", "Done: ",
            "Concluído.", "Done.",
            "Concluído", "Done",
            "Conjuntos de instruções", "Instruction sets",
            "Consumo (watts)", "Power (watts)",
            "Consumo do pacote", "Package power",
            "Continuar", "Continue",
            "ControlLib.dll não encontrada (instale o driver Intel Arc / Graphics mais recente)", "ControlLib.dll not found (install the latest Intel Arc / Graphics driver)",
            "Controlador embarcado", "Embedded controller",
            "Controladora", "Controller",
            "Controle (%)", "Control (%)",
            "Controle automático (curva original da placa) restaurado.", "Automatic control (card's original curve) restored.",
            "Controle da ventoinha", "Fan control",
            "Controle de ventoinha indisponível.", "Fan control unavailable.",
            "Copiado.", "Copied.",
            "Copiar nome", "Copy name",
            "Copiar valor", "Copy value",
            "Corrente", "Current",
            "Criando ponto de restauração (pode levar um minuto)...", "Creating restore point (may take a minute)...",
            "Crossbar / Uncore / Fabric clock: nenhuma API pública (NVML, NvAPI, ADL, IGCL) expõe esses clocks, então nenhum programa de terceiros consegue alterá-los.", "Crossbar / Uncore / Fabric clock: no public API (NVML, NvAPI, ADL, IGCL) exposes these clocks, só no third-party program can change them.",
            "DDR5: cada módulo tem 2 subcanais de 32 bits.", "DDR5: each module has two 32-bit sub-channels.",
            "DEP / NX (proteção de execução)", "DEP / NX (execution protection)",
            "Data do driver (registro)", "Driver date (registry)",
            "Data do driver", "Driver date",
            "Definir alerta (acima de...)", "Set alert (above...)",
            "Depois do download, execute o PawnIO_setup.exe (como administrador), conclua a instalação e abra o BunoTunner novamente.", "After downloading, run PawnIO_setup.exe (as administrator), finish the installation and open BunoTunner again.",
            "Desativar o Isolamento de núcleo reduz a proteção contra malware que ataca o núcleo do Windows. Desativar mesmo assim?", "Disabling Core isolation reduces protection against malware that attacks the Windows kernel. Disable anyway?",
            "Desconhecida", "Unknown",
            "Descrição: ", "Description: ",
            "Deseja abrir o download oficial agora?\n(Sim = baixa o PawnIO_setup.exe; Não = continuar sem ele. Este aviso aparece a cada abertura enquanto ele não estiver instalado.)", "Do you want to open the official download now?\n(Yes = downloads PawnIO_setup.exe; No = continue without it. This notice appears at every start while it is not installed.)",
            "Desgaste (uso da vida útil)", "Wear (life used)",
            "Desinstalador aberto: conclua as telas dele... (a busca de restos começa quando ele terminar)", "Uninstaller open: finish its screens... (the leftover search starts when it ends)",
            "Desinstalador terminou.", "Uninstaller finished.",
            "Desinstalar \"", "Uninstall \"",
            "Desligado (1 thread por núcleo)", "Off (1 thread per core)",
            "Desligado ou não confirmado (BAR1 de 256 MB)", "Off or unconfirmed (256 MB BAR1)",
            "Desligado", "Off",
            "Dica: marque a caixinha (ou de duplo clique) para ver o sensor no gráfico. Botão direito = alerta, copiar... Clique no cabeçalho para ordenar.", "Tip: tick the checkbox (or double-click) to see the sensor on the graph. Right-click = alert, copy... Click the header to sort.",
            "Digite parte do nome. Várias palavras = todas precisam aparecer (ex.: core temp). Ctrl+A seleciona tudo.", "Type part of the name. Several words = all must appear (e.g. core temp). Ctrl+A selects all.",
            "Discos", "Disks",
            "Driver SEM leitura da CPU (Integridade de memória/Defender?) - usando zona térmica do Windows", "Driver WITHOUT CPU reading (Memory integrity/Defender?) - using Windows thermal zone",
            "Duração", "Duration",
            "ECC da memória", "Memory ECC",
            "Em 'Até parar' o SSD recebe gravações contínuas (gera desgaste). Continuar?", "With 'Until stopped' the SSD receives continuous writes (causes wear). Continue?",
            "Enquanto isso o programa usa as zonas térmicas do Windows (menos precisas).\n\nAbrir agora a tela de Isolamento de núcleo?", "Meanwhile the program uses Windows thermal zones (less accurate).\n\nOpen the Core isolation screen now?",
            "Erro ao executar ", "Error running ",
            "Erro de escrita (", "Write error (",
            "Erro de leitura (", "Read error (",
            "Erro na busca: ", "Search error: ",
            "Erro: ", "Error: ",
            "Erros de escrita (total)", "Write errors (total)",
            "Erros de leitura (total)", "Read errors (total)",
            "Erros:", "Errors:",
            "Escolha (ou salve) um preset antes de criar a regra.", "Choose (or save) a preset before creating the rule.",
            "Escolha a pasta do programa", "Choose the program folder",
            "Escolha o cpuz.exe (versão portátil ou instalada)", "Choose cpuz.exe (portable or installed version)",
            "Escolha o que você quer ver: GPU, CPU, placa-mae, memória, discos, ventoinhas...", "Choose what you want to see: GPU, CPU, motherboard, memory, disks, fans...",
            "Escolha um preset para carregar núcleos e prioridade; depois clique em \"Aplicar aos marcados\". \"Salvar atual\" guarda o que está na grade.", "Choose a preset to load cores and priority; then click \"Apply to checked\". \"Save current\" stores what is in the grid.",
            "Escrita seq ", "Seq. write ",
            "Essa pasta e protegida pelo sistema e não pode ser usada aqui:\n", "This folder is protected by the system and cannot be used here:\n",
            "Esta placa AMD não aceitou controle de ventoinha pelo ADL.", "This AMD card did not accept fan control through ADL.",
            "Esta placa expõe todos os ajustes acima. ", "This card exposes all the settings above. ",
            "Esta ventoinha e somente leitura.", "This fan is read-only.",
            "Estabilidade", "Stability",
            "Estado de desempenho", "Performance state",
            "Estado informado pelo Windows", "State reported by Windows",
            "Estado: ATIVADA", "State: ENABLED",
            "Estado: ATIVADO", "State: ENABLED",
            "Estado: DESATIVADA", "State: DISABLED",
            "Estado: MPO DESATIVADO", "State: MPO DISABLED",
            "Estado: MPO padrão (ativo)", "State: MPO default (active)",
            "Estado: ativada", "State: enabled",
            "Estado: compressão ATIVADA", "State: compression ENABLED",
            "Estado: compressão DESATIVADA", "State: compression DISABLED",
            "Estado: desativada", "State: disabled",
            "Estado: desativado", "State: disabled",
            "Estado: menu clássico ATIVO", "State: classic menu ACTIVE",
            "Estado: menu padrão do Windows 11", "State: Windows 11 default menu",
            "Estado: overlay ATIVO", "State: overlay ACTIVE",
            "Estado: overlay desligado", "State: overlay off",
            "Estado: padrão do Windows (mitigações ativas)", "State: Windows default (mitigations active)",
            "Estado: personalizado (FeatureSettingsOverride=", "State: custom (FeatureSettingsOverride=",
            "Este item não tem desinstalador. Use 'Remoção forçada'.", "This item has no uninstaller. Use 'Forced removal'.",
            "Exclusivo", "Exclusive",
            "Exportar", "Export",
            "FALHOU - ", "FAILED - ",
            "FALHOU", "FAILED",
            "FPU - malha de ondas em ponto flutuante", "FPU - floating-point wave mesh",
            "Faixa da ventoinha (driver)", "Fan range (driver)",
            "Faixa de potência permitida", "Allowed power range",
            "Falha ao abrir o desinstalador: ", "Failed to open the uninstaller: ",
            "Falha ao salvar: ", "Failed to save: ",
            "Falhou (execute como administrador): ", "Failed (run as administrator): ",
            "Falhou: Overdrive8 indisponível", "Failed: Overdrive8 unavailable",
            "Falhou: ctlFanSetFixedSpeedMode erro 0x", "Failed: ctlFanSetFixedSpeedMode error 0x",
            "Falhou: função ausente", "Failed: function missing",
            "Falhou: ", "Failed: ",
            "Falhou", "Failed",
            "Família / modelo / stepping", "Family / model / stepping",
            "Ventoinha (RPM agora)", "Fan (RPM now)",
            "Ventoinha (% agora)", "Fan (% now)",
            "Filtrar por tipo de medida (temperatura, uso, clock...)", "Filter by measurement type (temperature, usage, clock...)",
            "Finaliza todos os programas abertos que rodam da pasta do programa selecionado (e para os serviços dele). Use antes de desinstalar se ele estiver aberto.", "Ends all open programs that run from the selected program's folder (and stops its services). Use before uninstalling if it is open.",
            "Finalizando o teste anterior... tente de novo em um instante.", "Ending the previous test... try again in a moment.",
            "Finalizar todos os processos de \"", "End all processes of \"",
            "Fonte: ", "Source: ",
            "Fonte", "Source",
            "Fuso horário: ", "Time zone: ",
            "GPU não encontrada", "GPU not found",
            "GPU - consumo", "GPU - power",
            "GPU - render 3D de pelos (OpenGL)", "GPU - 3D fur render (OpenGL)",
            "GPU - temperatura", "GPU - temperature",
            "GPU - uso", "GPU - usage",
            "GPUs listadas", "GPUs listed",
            "Gabinete", "Case",
            "Ha sinais de desgaste, erros ou temperatura alta. Mantenha backup em dia e acompanhe.", "There are signs of wear, errors or high temperature. Keep your backups up to date and monitor it.",
            "Hibernação ATIVADA.", "Hibernation ENABLED.",
            "Hibernação DESATIVADA (hiberfil.sys removido).", "Hibernation DISABLED (hiberfil.sys removed).",
            "Histórico do gráfico (CSV)...", "Graph history (CSV)...",
            "Horas ligado", "Power-on hours",
            "Host reverso: ", "Reverse host: ",
            "Hypervisor ativo (Hyper-V / WSL2)", "Hypervisor active (Hyper-V / WSL2)",
            "ID de hardware (PNP)", "Hardware ID (PNP)",
            "ID do processador", "Processor ID",
            "IP externo: ", "External IP: ",
            "IP preferido (v6 se houver): ", "Preferred IP (v6 if available): ",
            "Identificação concluída: RPM máximo guardado (usado em \"Definir por RPM\").", "Identification finished: max RPM saved (used in \"Set by RPM\").",
            "Identificador", "Identifier",
            "Identificar (100% por 5 s)", "Identify (100% for 5 s)",
            "Identificar = liga as selecionadas a 100% por 5 s para você descobrir qual é qual.  Botão direito na lista: selecionar por tipo (CPU, bombas, gabinete) ou renomear (inclua CPU, Bomba ou Gabinete no nome).", "Identify = runs the selected fans at 100% for 5 s so you can tell which is which.  Right-click the list: select by type (CPU, pumps, case) or rename (include CPU, Pump or Case in the name).",
            "Idioma:", "Language:",
            "Imagem do gráfico (PNG)...", "Graph image (PNG)...",
            "Indisponível (a leitura exata só existe no driver NVIDIA; veja no AMD Adrenalin: Smart Access Memory, ou na BIOS)", "Unavailable (the exact reading only exists in the NVIDIA driver; check AMD Adrenalin: Smart Access Memory, or the BIOS)",
            "Indisponível (placa sem ECC)", "Unavailable (board without ECC)",
            "Indisponível (placa/driver não expõe)", "Unavailable (board/driver does not expose it)",
            "Indisponível (", "Unavailable (",
            "Indisponível: ", "Unavailable: ",
            "Indisponível", "Unavailable",
            "Informe min e max válidos.", "Enter valid min and max.",
            "Inicialização rápida (Fast Startup)", "Fast Startup",
            "Inicialização", "Startup",
            "Iniciando...", "Starting...",
            "Iniciar / parar o estresse de CPU", "Start / stop the CPU stress",
            "Iniciar / parar o estresse de FPU", "Start / stop the FPU stress",
            "Iniciar / parar o estresse de GPU", "Start / stop the GPU stress",
            "Iniciar / parar o estresse de RAM", "Start / stop the RAM stress",
            "Iniciar / parar o estresse de SSD", "Start / stop the SSD stress",
            "Instalar PawnIO", "Install PawnIO",
            "Integridade de memória (HVCI)", "Memory integrity (HVCI)",
            "Intel (soquete não identificado)", "Intel (socket not identified)",
            "Intel IGCL (experimental): o driver Intel valida os limites; valores fora da faixa são recusados.", "Intel IGCL (experimental): the Intel driver validates the limits; out-of-range values are rejected.",
            "Interrompido pelo usuário.", "Interrupted by the user.",
            "Isolamento de núcleo ATIVADO. Reinicie o PC.", "Core isolation ENABLED. Restart the PC.",
            "Isolamento de núcleo DESATIVADO. Reinicie o PC.", "Core isolation DISABLED. Restart the PC.",
            "Janela de render fechada: teste interrompido.", "Render window closed: test interrupted.",
            "LHM aberto (CPU, GPU, placa-mae, memória)", "LHM opened (CPU, GPU, motherboard, memory)",
            "Largura de endereço", "Address width",
            "Latência da RAM (bloco de ", "RAM latency (block of ",
            "Latência real do CAS: ", "Actual CAS latency: ",
            "Leitura de sensores", "Sensor reading",
            "Leitura dos sensores da CPU", "CPU sensor reading",
            "Leitura", "Reading",
            "Lendo a lista de programas instalados...", "Reading the list of installed programs...",
            "Lendo informações da CPU...", "Reading CPU information...",
            "Lendo módulos...", "Reading modules...",
            "Lendo os discos (Windows Storage + S.M.A.R.T.)...", "Reading disks (Windows Storage + S.M.A.R.T.)...",
            "Lendo os timings direto do controlador de memória...", "Reading timings directly from the memory controller...",
            "Ligado (exigido pelo BAR grande)", "On (required by the large BAR)",
            "Ligado: cada linha usa a própria escala (0-100%). Desligado: mesma escala para todas.", "On: each line uses its own scale (0-100%). Off: same scale for all.",
            "Ligado - faixa ", "On - range ",
            "Ligado (", "On (",
            "Ligado", "On",
            "Limitação de clock agora", "Current clock limit",
            "Limite de potência (TDP)", "Power limit (TDP)",
            "Limite de potência atual", "Current power limit",
            "Limite de potência padrão", "Default power limit",
            "Limite de potência", "Power limit",
            "Limite de temperatura", "Temperature limit",
            "Limpar histórico", "Clear history",
            "Localização aproximada: ", "Approximate location: ",
            "MPO desativado. Reinicie o PC.", "MPO disabled. Restart the PC.",
            "MPO restaurado ao padrão. Reinicie o PC.", "MPO restored to default. Restart the PC.",
            "Manter a atual", "Keep current",
            "Marque (caixinha) os processos que quer alterar. Sem nenhum marcado, vale a seleção com Ctrl/Shift.", "Check the processes you want to change. With none checked, the Ctrl/Shift selection is used.",
            "Marque (caixinha) ou selecione pelo menos um processo na lista.", "Check or select at least one process in the list.",
            "Marque pelo menos um CPU antes de salvar.", "Check at least one CPU before saving.",
            "Marque pelo menos um CPU na grade.", "Check at least one CPU in the grid.",
            "Marque sensores na lista (ou use 'Essenciais') para ver o gráfico", "Check sensors in the list (or use 'Essentials') to see the graph",
            "Marque sensores para o gráfico primeiro.", "Check sensors for the graph first.",
            "Media:", "Average:",
            "Medindo (feche outros programas para um resultado melhor)...", "Measuring (close other programs for a better result)...",
            "Medindo download...", "Measuring download...",
            "Medindo latência...", "Measuring latency...",
            "Medindo upload...", "Measuring upload...",
            "Memória RAM", "RAM",
            "Memória de video (VRAM)", "Video memory (VRAM)",
            "Memória livre insuficiente para o teste.", "Not enough free memory for the test.",
            "Memória/Dados", "Memory/Data",
            "Memória: ", "Memory: ",
            "Menu clássico ativado. Reinicie o Explorer ou faça logoff.", "Classic menu enabled. Restart Explorer or sign out.",
            "Menu padrão do Windows 11 restaurado. Reinicie o Explorer ou faça logoff.", "Windows 11 default menu restored. Restart Explorer or sign out.",
            "Metade", "Half",
            "Mitigações DESATIVADAS. Reinicie o PC.", "Mitigations DISABLED. Restart the PC.",
            "Mitigações restauradas ao padrão do Windows. Reinicie o PC.", "Mitigations restored to the Windows default. Restart the PC.",
            "Mobile (soldada)", "Mobile (soldered)",
            "Modelo", "Model",
            "Modo de computação", "Compute mode",
            "Modo de jogo (Game Mode)", "Game Mode",
            "Monitor conectado / display ativo", "Connected monitor / active display",
            "Mostrar / ocultar no gráfico", "Show / hide on graph",
            "Motivos: ", "Reasons: ",
            "Muda o visual do programa: Luna (XP), Frutiger Aero (Win7), Dark/AMOLED, Neon, Steampunk ou Vintage (Windows 95)", "Changes the program's look: Luna (XP), Frutiger Aero (Win7), Dark/AMOLED, Neon, Steampunk or Vintage (Windows 95)",
            "NÃO (rode como administrador)", "NO (run as administrator)",
            "Nada sobrou: nenhum resto encontrado para ", "Nothing left: no leftovers found for ",
            "Não consegui criar o arquivo de teste em ", "Could not create the test file in ",
            "Não foi possível abrir o navegador: ", "Could not open the browser: ",
            "Não foi possível consultar o IP externo: ", "Could not look up the external IP: ",
            "Não foi possível criar o ponto de restauração (o Windows limita 1 a cada 24 h, ou a proteção do sistema está desligada). Continuando.", "Could not create the restore point (Windows limits it to 1 per 24 h, or system protection is off). Continuing.",
            "Não foi possível instalar automaticamente (", "Could not install automatically (",
            "Não foi possível ler: ", "Could not read: ",
            "Não foi possível medir (sem internet ou bloqueio de rede?): ", "Could not measure (no internet or network block?): ",
            "Não foi possível remover:\n\n", "Could not remove:\n\n",
            "Não foi possível restaurar o modo automático.", "Could not restore automatic mode.",
            "Não sei qual é a pasta deste programa; use 'Escolher pasta ou .exe' para informar.", "I don't know this program's folder; use 'Choose folder or .exe' to tell me.",
            "Navegadores e jogos criam vários processos com o mesmo nome; isto altera todos de uma vez.", "Browsers and games create several processes with the same name; this changes all of them at once.",
            "Nenhum ajuste disponível nesta placa.", "No adjustments available on this card.",
            "Nenhum backend de overclock para esta GPU. ", "No overclock backend for this GPU. ",
            "Nenhum drive disponível para o teste de SSD.", "No drive available for the SSD test.",
            "Nenhum item marcado.", "No item checked.",
            "Nenhum módulo informado pelo Windows (WMI)", "No module reported by Windows (WMI)",
            "Nenhum processo desse programa esta aberto.", "No process of this program is open.",
            "Nenhum resultado para copiar ainda.", "No result to copy yet.",
            "Nenhum resultado para salvar ainda.", "No result to save yet.",
            "Nenhuma GPU encontrada pelo OpenCL.", "No GPU found by OpenCL.",
            "Nenhuma GPU selecionada.", "No GPU selected.",
            "Nenhuma placa de video foi detectada.\nRode como administrador e confira a pasta libs.", "No graphics card was detected.\nRun as administrator and check the libs folder.",
            "Nenhuma ventoinha controlável do tipo \"", "No controllable fan of type \"",
            "Nenhuma ventoinha encontrada", "No fan found",
            "Nenhuma", "None",
            "Nome (inclua CPU, Bomba ou Gabinete para definir o tipo):", "Name (include CPU, Pump or Case to set the type):",
            "Nome do PC: ", "PC name: ",
            "Nome do hardware não identificado.", "Hardware name not identified.",
            "Nome do preset (ex.: Jogo - núcleos físicos):", "Preset name (e.g. Game - physical cores):",
            "Nome do processo (ex.: game.exe). Preset: ", "Process name (e.g. game.exe). Preset: ",
            "Nome", "Name",
            "Normalizar escala", "Normalize scale",
            "Núcleo: ", "Core: ",
            "Núcleos (CPUs lógicos) que os processos marcados podem usar", "Cores (logical CPUs) the checked processes may use",
            "Núcleos CUDA", "CUDA cores",
            "Núcleos físicos", "Physical cores",
            "NvAPI EnumGPUs erro ", "NvAPI EnumGPUs error ",
            "NvAPI SetPstates20 erro ", "NvAPI SetPstates20 error ",
            "NvAPI indisponível", "NvAPI unavailable",
            "NvAPI sem SetPstates20", "NvAPI without SetPstates20",
            "NvAPI_Initialize erro ", "NvAPI_Initialize error ",
            "Não consegui ler: ", "Could not read: ",
            "O PawnIO e um driver assinado e de código aberto que o LibreHardwareMonitor (usado pelo BunoTunner) precisa para acessar o hardware. ", "PawnIO is a signed, open-source driver that LibreHardwareMonitor (used by BunoTunner) needs to access the hardware. ",
            "O PawnIO já está instalado.", "PawnIO is already installed.",
            "O Windows não informou S.M.A.R.T. deste disco (comum em discos USB e controladoras RAID).", "Windows did not report S.M.A.R.T. for this disk (common on USB disks and RAID controllers).",
            "O instalador foi fechado, mas o PawnIO não foi detectado. Tente novamente.", "The installer was closed, but PawnIO was not detected. Try again.",
            "O link do ", "The link for ",
            "OC das GPUs (NVML/ADL/IGCL)", "GPU OC (NVML/ADL/IGCL)",
            "OK (ADL Overdrive6): ventoinha em ", "OK (ADL Overdrive6): fan at ",
            "OK (ADL Overdrive8): velocidade mínima da ventoinha em ", "OK (ADL Overdrive8): minimum fan speed at ",
            "OK (IGCL): ventoinha em ", "OK (IGCL): fan at ",
            "OK (NVML): ventoinha(s) em ", "OK (NVML): fan(s) at ",
            "OK: automático", "OK: automatic",
            "Observação", "Note",
            "Ocultar sem leitura", "Hide without reading",
            "Offset do clock da memória, em MHz", "Memory clock offset, in MHz",
            "Offset do clock da memória", "Memory clock offset",
            "Offset do clock do núcleo, em MHz acima do padrão", "Core clock offset, in MHz above default",
            "Offset do clock do núcleo", "Core clock offset",
            "Ok - driver de sensores funcionando", "OK - sensor driver working",
            "OpenCL não encontrado (instale o driver da placa de video).", "OpenCL not found (install the graphics driver).",
            "OpenCL: erro ao compilar (", "OpenCL: compile error (",
            "OpenCL: erro ", "OpenCL: error ",
            "OpenCL: falha ao carregar o programa (", "OpenCL: failed to load the program (",
            "OpenCL: falha ao criar contexto (", "OpenCL: failed to create context (",
            "OpenCL: falha na fila de comandos (", "OpenCL: command queue failure (",
            "OpenCL: sem memória de video para o teste.", "OpenCL: not enough video memory for the test.",
            "OpenGL indisponível (", "OpenGL unavailable (",
            "Os sensores da CPU (temperatura, tensão, consumo) precisam do driver do LibreHardwareMonitor, e ele não está conseguindo ler esta CPU.\n\n", "CPU sensors (temperature, voltage, power) need the LibreHardwareMonitor driver, and it is unable to read this CPU.\n\n",
            "Outra", "Other",
            "Outros", "Others",
            "Outro", "Other",
            "Overclock / limites - ", "Overclock / limits - ",
            "Overclock / ventoinha", "Overclock / fan",
            "Overclock indisponível para esta GPU. ", "Overclock unavailable for this GPU. ",
            "Overdrive indisponível nesta placa/driver (ative \"Ajuste manual\" no AMD Adrenalin)", "Overdrive unavailable on this card/driver (enable \"Manual tuning\" in AMD Adrenalin)",
            "Overdrive6_FanSpeed_Reset erro ", "Overdrive6_FanSpeed_Reset error ",
            "Overdrive6_FanSpeed_Set erro ", "Overdrive6_FanSpeed_Set error ",
            "Overdrive8: núcleo = clock máximo; memória = clock máximo (MHz reais); potência em % relativo ao padrão.", "Overdrive8: core = max clock; memory = max clock (real MHz); power in % relative to default.",
            "Overdrive8_Init_Setting_Get erro ", "Overdrive8_Init_Setting_Get error ",
            "Overdrive8_Setting_Set erro ", "Overdrive8_Setting_Set error ",
            "Overlay do DLSS ativado.", "DLSS overlay enabled.",
            "Overlay do DLSS desativado.", "DLSS overlay disabled.",
            "PAUSADO", "PAUSED",
            "PCIe (agora)", "PCIe (now)",
            "PCIe (máximo da placa)", "PCIe (card maximum)",
            "PCIe - geração", "PCIe - generation",
            "PCIe - largura (x)", "PCIe - width (x)",
            "Padrão do Windows (não alterado)", "Windows default (unchanged)",
            "Padrão", "Default",
            "Parado por segurança: temperatura de ", "Stopped for safety: temperature of ",
            "Passe o mouse sobre o gráfico para ver os valores de cada instante", "Hover over the graph to see the values at each moment",
            "Pastas e arquivos irão para a Lixeira.\n", "Folders and files will go to the Recycle Bin.\n",
            "Pastas e arquivos serão APAGADOS de vez.\n", "Folders and files will be DELETED permanently.\n",
            "Pausar", "Pause",
            "PawnIO instalado! Feche e abra o BunoTunner para os sensores, ventoinhas e timings passarem a usar o driver.", "PawnIO installed! Close and reopen BunoTunner so sensors, fans and timings start using the driver.",
            "PawnIO instalado: ", "PawnIO installed: ",
            "PawnIO instalado", "PawnIO installed",
            "PawnIO não instalado; ", "PawnIO not installed; ",
            "PawnIO: driver usado pelo LibreHardwareMonitor para ler sensores da CPU/placa-mae e controlar as ventoinhas da placa-mae. Clique para baixar e instalar.", "PawnIO: driver used by LibreHardwareMonitor to read CPU/motherboard sensors and control motherboard fans. Click to download and install.",
            "PawnIOLib.dll não encontrada; ", "PawnIOLib.dll not found; ",
            "PawnIOLib.dll sem as funções esperadas; ", "PawnIOLib.dll without the expected functions; ",
            "Pico:", "Peak:",
            "Placa-mae (Super I/O)", "Motherboard (Super I/O)",
            "Placa-mae", "Motherboard",
            "Ponto de restauração criado.", "Restore point created.",
            "Por que?\n", "Why?\n",
            "Potência", "Power",
            "Pouco espaço livre no drive ", "Little free space on drive ",
            "Processador - Zona térmica (Windows/ACPI)", "Processor - Thermal zone (Windows/ACPI)",
            "Processador de video", "Video processor",
            "Processador", "Processor",
            "Procurando restos de ", "Looking for leftovers of ",
            "Procurando restos...", "Looking for leftovers...",
            "Proibido", "Forbidden",
            "Pronto: ", "Ready: ",
            "Pronto", "Ready",
            "Provedor (ISP/ASN): ", "Provider (ISP/ASN): ",
            "Quando o processo abrir, o preset é aplicado sozinho (ex.: jogo.exe -> núcleos físicos).", "When the process starts, the preset is applied automatically (e.g. game.exe -> physical cores).",
            "Quantidade de ventoinhas", "Number of fans",
            "RAM - mapa de memória", "RAM - memory map",
            "RAM em uso", "RAM in use",
            "REMOÇÃO FORÇADA de \"", "FORCED REMOVAL of \"",
            "RPM = giros por minuto. % = potência enviada. \"só leitura\" = a placa-mae não permite controle por software.", "RPM = revolutions per minute. % = power sent. \"read-only\" = the motherboard does not allow software control.",
            "Recomendamos a instalação do PawnIO.\n\n", "We recommend installing PawnIO.\n\n",
            "Redefinir: ", "Reset: ",
            "Registro", "Registry",
            "Regra aplicada: ", "Rule applied: ",
            "Removendo...", "Removing...",
            "Remover alerta", "Remove alert",
            "Remover ", "Remove ",
            "Renomear ventoinha", "Rename fan",
            "Renomear...", "Rename...",
            "Resolução atual", "Current resolution",
            "Resultados copiados.", "Results copied.",
            "Revisão", "Revision",
            "Ring0 (LHM) com PCI: ", "Ring0 (LHM) with PCI: ",
            "Roda CPU, FPU, RAM e GPU um após o outro, cada um com a duração do seu bloco (o SSD fica de fora para não gerar desgaste).", "Runs CPU, FPU, RAM and GPU one after another, each with its block's duration (the SSD is left out to avoid wear).",
            "Rodando...", "Running...",
            "Ruim", "Poor",
            "SEGURANÇA: ", "SECURITY: ",
            "SEGURANÇA", "SECURITY",
            "SLAT (paginação aninhada)", "SLAT (nested paging)",
            "SSD - mapa de blocos do disco", "SSD - disk block map",
            "Salvar imagem do render (PNG)", "Save render image (PNG)",
            "Salvo em ", "Saved to ",
            "Salvo: ", "Saved: ",
            "Se esquentar demais, as ventoinhas vão a 100% mesmo no modo manual.", "If it gets too hot, the fans go to 100% even in manual mode.",
            "Segundos", "Seconds",
            "Segurança baseada em virtualização (VBS)", "Virtualization-based security (VBS)",
            "Segurança: forcar 100% se a temperatura passar de", "Safety: force 100% if the temperature goes above",
            "Selecionadas a 100% por 5 s. Veja/ouça qual acelerou e acompanhe o RPM na lista.", "Selected ones at 100% for 5 s. See/hear which one sped up and follow the RPM in the list.",
            "Selecionado", "Selected",
            "Selecionar só as controláveis", "Select only the controllable ones",
            "Selecionar todas", "Select all",
            "Selecionar: CPU", "Select: CPU",
            "Selecionar: GPU", "Select: GPU",
            "Selecionar: bombas / AIO", "Select: pumps / AIO",
            "Selecionar: gabinete", "Select: case",
            "Selecione na lista pelo menos uma ventoinha controlável.", "Select at least one controllable fan in the list.",
            "Selecione pelo menos uma ventoinha controlável.", "Select at least one controllable fan.",
            "Selecione uma ventoinha para renomear.", "Select a fan to rename.",
            "Sem cópia do registro.", "No registry copy.",
            "Sem dados", "No data",
            "Sem ele ficam sem leitura: vários sensores de temperatura da CPU e da placa-mae, o controle das ventoinhas ligadas a placa-mae e os timings da memória RAM.\n\n", "Without it these have no reading: several CPU and motherboard temperature sensors, control of the fans connected to the motherboard, and the RAM timings.\n\n",
            "Sem leitura de temperatura - veja o LEIA-ME (driver bloqueado?)", "No temperature reading - see the README (driver blocked?)",
            "Sensores atualizando a cada ", "Sensors updating every ",
            "Sensores da CPU sem leitura", "CPU sensors without reading",
            "Sensores expostos (LHM)", "Exposed sensors (LHM)",
            "Serviço", "Service",
            "Servidor DHCP: ", "DHCP server: ",
            "Só no gráfico", "Only on graph",
            "Soquete", "Socket",
            "Sufixo DNS: ", "DNS suffix: ",
            "Tarefa", "Task",
            "Taxa de atualização do monitor", "Monitor refresh rate",
            "Taxa de dados", "Data rate",
            "Taxa (", "Rate (",
            "Temp Limit (alvo térmico)", "Temp Limit (thermal target)",
            "Temp. alvo (limite de temperatura)", "Target temp. (temperature limit)",
            "Temp. de desligamento", "Shutdown temp.",
            "Temp. de slowdown (throttle)", "Slowdown temp. (throttle)",
            "Temp. máxima da GPU", "Max GPU temp.",
            "Temp. máxima da memória", "Max memory temp.",
            "Temperatura do núcleo", "Core temperature",
            "Temperatura máxima registrada", "Max recorded temperature",
            "Temperatura hotspot", "Hotspot temperature",
            "Temperatura: ", "Temperature: ",
            "Temperatura", "Temperature",
            "Tempo entre as leituras dos sensores (100 a 10000 ms). Menos = mais fluido, mas usa mais CPU.", "Time between sensor readings (100 to 10000 ms). Lower = smoother, but uses more CPU.",
            "Tempo:", "Time:",
            "Teste cancelado.", "Test cancelled.",
            "Teste concluído.", "Test finished.",
            "Teste finalizado.", "Test ended.",
            "Threads (processadores lógicos)", "Threads (logical processors)",
            "Tipo (LHM)", "Type (LHM)",
            "Tipo / interface", "Type / interface",
            "Tipo: ", "Type: ",
            "Tipo:", "Type:",
            "Todas as ventoinhas voltaram ao automático.", "All fans went back to automatic.",
            "Todos os tipos", "All types",
            "Todos (", "All (",
            "Todas", "All",
            "Tudo certo. Em HDDs, observe erros e temperatura (ideal abaixo de 50 C).", "All good. On HDDs, watch errors and temperature (ideally below 50 C).",
            "Tudo certo. SSDs perdem vida conforme a quantidade de dados gravados; valores abaixo de 30% pedem atenção.", "All good. SSDs lose life according to the amount of data written; values below 30% need attention.",
            "Uma cópia das chaves do registro será salva em BunoTunner_backup.", "A copy of the registry keys will be saved in BunoTunner_backup.",
            "Usando ", "Using ",
            "Use isto para programas que não aparecem na lista (portáteis, restos de algo já removido, etc.).\n\nO que você quer escolher?", "Use this for programs that do not appear in the list (portable ones, leftovers of something already removed, etc.).\n\nWhat do you want to choose?",
            "Uso (%)", "Usage (%)",
            "Uso do controlador de memória", "Memory controller usage",
            "Uso do núcleo", "Core usage",
            "Uso total", "Total usage",
            "Usuário: ", "User: ",
            "VRAM em uso (%)", "VRAM in use (%)",
            "VRAM usada", "VRAM used",
            "Valor inválido.", "Invalid value.",
            "Valores atuais (CSV)...", "Current values (CSV)...",
            "Valores originais restaurados.", "Original values restored.",
            "Variação", "Variation",
            "Velocidade:", "Speed:",
            "Ventoinha (%)", "Fan (%)",
            "Ventoinha (RPM)", "Fan (RPM)",
            "Ventoinha da CPU", "CPU fan",
            "Ventoinhas da GPU", "GPU fans",
            "Ventoinhas", "Fans",
            "Versão do driver (Windows)", "Driver version (Windows)",
            "Virtualização no firmware (VT-x / AMD-V)", "Virtualization in firmware (VT-x / AMD-V)",
            "Voltagem:", "Voltage:",
            "Voltagem", "Voltage",
            "WMI indisponível: ", "WMI unavailable: ",
            "Zerar min/max deste sensor", "Reset min/max of this sensor",
            "Zona térmica", "Thermal zone",
            "Zona ", "Zone ",
            "\" (pasta ", "\" (folder ",
            "\" (renomeie as suas para classificar).", "\" (rename yours to classify).",
            "\" passar de (", "\" goes above (",
            "\" salvo.", "\" saved.",
            "\" selecionada(s).", "\" selected.",
            "\"?\n\n1) O desinstalador oficial será aberto (siga as telas dele).\n2) Depois o BunoTunner procura o que sobrou para você revisar.", "\"?\n\n1) The official uninstaller will open (follow its screens).\n2) Then BunoTunner looks for what was left behind for you to review.",
            "\"\n\nIsto NÃO usa o desinstalador: encerra os processos do programa, para os serviços dele e lista tudo o que for dele (pasta, registro, inicialização, tarefas) para você apagar.\n\nNada e apagado antes de você revisar e confirmar a lista. Continuar?", "\"\n\nThis does NOT use the uninstaller: it ends the program's processes, stops its services and lists everything that belongs to it (folder, registry, startup, tasks) for you to delete.\n\nNothing is deleted before you review and confirm the list. Continue?",
            "\n(copiado para a área de transferência)", "\n(copied to the clipboard)",
            "\n(tempo esgotado)", "\n(timed out)",
            "\n\nSe o arquivo está em uso, feche o programa (ou use 'Remoção forçada') e tente de novo; ou reinicie o PC.", "\n\nIf the file is in use, close the program (or use 'Forced removal') and try again; or restart the PC.",
            "a LibreHardwareMonitorLib.dll da pasta libs não traz o módulo PawnIO (DLL antiga: atualize); ", "the LibreHardwareMonitorLib.dll in the libs folder does not include the PawnIO module (old DLL: update it); ",
            "acesso negado (rode como administrador)", "access denied (run as administrator)",
            "adaptador AMD não encontrado no ADL", "AMD adapter not found in ADL",
            "alta", "high",
            "media", "medium",
            "argumento inválido", "invalid argument",
            "arquivo protegido", "protected file",
            "atiadlxx.dll não encontrada (instale o driver Radeon/Adrenalin)", "atiadlxx.dll not found (install the Radeon/Adrenalin driver)",
            "chave inválida", "invalid key",
            "chave protegida", "protected key",
            "clock de aplicativo", "application clock",
            "código ", "code ",
            "controlador de memória da CPU (AMD Ryzen, canal ", "CPU memory controller (AMD Ryzen, channel ",
            "desligado no .ini (vendoroc=0)", "disabled in the .ini (vendoroc=0)",
            "distância até o TjMax", "distance to TjMax",
            "em uso e não foi possível agendar a exclusão", "in use and the deletion could not be scheduled",
            "entrada em Programas e Recursos", "entry in Programs and Features",
            "erro ao inspecionar a DLL: ", "error inspecting the DLL: ",
            "erro genérico", "generic error",
            "erro - ", "error - ",
            "erro ", "error ",
            "esta placa usa Overdrive ", "this card uses Overdrive ",
            "esta plataforma (", "this platform (",
            "este ajuste não e exposto pelo driver Intel desta GPU", "this setting is not exposed by this GPU's Intel driver",
            "fabricante sem suporte de OC nesta versão", "manufacturer without OC support in this version",
            "falha ao carregar PawnIOLib.dll; ", "failed to load PawnIOLib.dll; ",
            "falhou", "failed",
            "fase 2 concluída (+", "phase 2 finished (+",
            "fase 2 erro: ", "phase 2 error: ",
            "fase 2: ", "phase 2: ",
            "feature desligada no Adrenalin", "feature turned off in Adrenalin",
            "formato de pixel OpenGL indisponível", "OpenGL pixel format unavailable",
            "freio de energia (HW)", "power brake (HW)",
            "frontal", "front",
            "gabinete", "case",
            "gráfico", "graph",
            "indisponível", "unavailable",
            "janela mostrada", "window shown",
            "leitura direta do controlador: só CPUs AMD Ryzen nesta versão", "direct controller reading: only AMD Ryzen CPUs in this version",
            "limite de potência (SW)", "power limit (SW)",
            "limite de potência fixo nesta placa (normal em notebooks: o fabricante trava o TDP)", "fixed power limit on this card (normal on laptops: the manufacturer locks the TDP)",
            "limite de temperatura não ajustável nesta placa", "temperature limit not adjustable on this card",
            "limite térmico (HW)", "thermal limit (HW)",
            "limite térmico (SW)", "thermal limit (SW)",
            "não foi possível criar o contexto OpenGL", "could not create the OpenGL context",
            "não foi possível ler o controlador de memória: ", "could not read the memory controller: ",
            "não suportado nesta GPU/notebook", "not supported on this GPU/laptop",
            "não suportado nesta placa", "not supported on this card",
            "não suportado", "not supported",
            "nenhum adaptador ADL", "no ADL adapter",
            "nenhum adaptador AMD no ADL (", "no AMD adapter in ADL (",
            "nenhum dispositivo Intel (ctlEnumerateDevices 0x", "no Intel device (ctlEnumerateDevices 0x",
            "nvml.dll não encontrada", "nvml.dll not found",
            "não informado pela BIOS", "not reported by the BIOS",
            "o Windows não informou discos", "Windows did not report disks",
            "o driver Intel não liberou overclock para esta GPU (só Arc dedicadas; gráficos integrados não tem)", "the Intel driver did not enable overclocking for this GPU (dedicated Arc only; integrated graphics do not have it)",
            "o driver não informa a faixa (comum em notebooks); valores acima do limite serão recusados", "the driver does not report the range (common on laptops); values above the limit will be rejected",
            "o driver não liberou este ajuste (", "the driver did not enable this setting (",
            "ociosa (normal)", "idle (normal)",
            "pawnio_load falhou (0x", "pawnio_load failed (0x",
            "pawnio_open falhou (0x", "pawnio_open failed (0x",
            "recurso inexistente neste driver", "feature not present in this driver",
            "registradores do controlador não reconhecidos nesta CPU (use o botão Diagnóstico)", "controller registers not recognized on this CPU (use the Diagnostics button)",
            "relatório do CPU-Z (", "CPU-Z report (",
            "rode como Administrador", "run as Administrator",
            "sem backend para este fabricante", "no backend for this manufacturer",
            "sem driver NVIDIA", "no NVIDIA driver",
            "sem implementação", "not implemented",
            "sem permissão no registro: ", "no registry permission: ",
            "sem reset", "no reset",
            "sensores prontos (fase 1)", "sensors ready (phase 1)",
            "slowdown de hardware", "hardware slowdown",
            "tipo desconhecido", "unknown type",
            "traseira", "rear",
            "versão de estrutura incompatível", "incompatible structure version",
            "papel de parede", "wallpaper",
            "sim", "yes",
            "não", "no",
            "Sim", "Yes",
            "Grupo;Sensor;Tipo;Valor;Mínimo;Máximo;Unidade", "Group;Sensor;Type;Value;Minimum;Maximum;Unit",
            "IGCL incompleta", "IGCL incomplete",
            "IGCL sem Overclock", "IGCL without Overclock",
            "Sistema operacional", "Operating system",
            "Driver (registro)", "Driver (registry)",
            "pasta de instalação", "installation folder",
            "pasta protegida", "protected folder",
            "Temp max", "Max temp",
            "NVML iniciado", "NVML started",
            "Rede", "Network",
            "Placa de video", "Graphics card",
            " MB em ", " MB in ",
            "LHM configurado", "LHM configured",
            "MainForm: início", "MainForm: start",
            "MainForm: páginas criadas", "MainForm: pages created",
            "SensorHub.Open concluído", "SensorHub.Open done",
            "tema e PawnIO verificados", "theme and PawnIO checked",
            "ns x tamanho do bloco", "ns x block size" };

        static Lang()
        {
            for (int i = 0; i + 1 < Pairs.Length; i += 2) en[Pairs[i]] = Pairs[i + 1];
            for (int i = 0; i + 1 < Pairs2.Length; i += 2) en[Pairs2[i]] = Pairs2[i + 1];
            Dictionary<char, List<string>> tmp = new Dictionary<char, List<string>>();
            foreach (string k in en.Keys)
            {
                if (k.Length == 0 || k == en[k]) continue;
                List<string> l;
                if (!tmp.TryGetValue(k[0], out l)) { l = new List<string>(); tmp[k[0]] = l; }
                l.Add(k);
            }
            foreach (KeyValuePair<char, List<string>> kv in tmp)
            {
                kv.Value.Sort(delegate(string a, string b) { return b.Length != a.Length ? b.Length.CompareTo(a.Length) : string.CompareOrdinal(a, b); });
                byFirst[kv.Key] = kv.Value.ToArray();
            }
        }

        public static void Init()
        {
            string v = Settings.Get("lang", "auto");
            if (v == "pt") Cur = 0;
            else if (v == "en") Cur = 1;
            else Cur = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower() == "pt" ? 0 : 1;
            lock (cache) cache.Clear();
        }

        static bool IsW(char c) { return char.IsLetterOrDigit(c); }

        // traduz um texto qualquer (inteiro ou so trechos conhecidos dele); em portugues devolve o proprio texto
        public static string Pick(string pt, string en_) { return Cur == 1 ? en_ : pt; }

        public static string T(string pt)
        {
            if (Cur == 0 || string.IsNullOrEmpty(pt)) return pt;
            string r;
            if (en.TryGetValue(pt, out r)) return r;
            lock (cache) { if (cache.TryGetValue(pt, out r)) return r; }
            r = Frag(pt);
            lock (cache) { if (cache.Count > 6000) cache.Clear(); cache[pt] = r; }
            return r;
        }

        static string Frag(string s)
        {
            StringBuilder sb = null;
            int last = 0, i = 0, n = s.Length;
            while (i < n)
            {
                string[] ks;
                if (byFirst.TryGetValue(s[i], out ks) && !(i > 0 && IsW(s[i - 1]) && IsW(s[i])))
                {
                    string hit = null;
                    for (int j = 0; j < ks.Length; j++)
                    {
                        string k = ks[j];
                        if (i + k.Length > n) continue;
                        if (string.CompareOrdinal(s, i, k, 0, k.Length) != 0) continue;
                        int e = i + k.Length;
                        if (IsW(k[k.Length - 1]) && e < n && IsW(s[e])) continue;
                        hit = k; break;
                    }
                    if (hit != null)
                    {
                        if (sb == null) sb = new StringBuilder(n + 24);
                        sb.Append(s, last, i - last).Append(en[hit]);
                        i += hit.Length; last = i;
                        continue;
                    }
                }
                i++;
            }
            if (sb == null) return s;
            sb.Append(s, last, n - last);
            return sb.ToString();
        }

        static Holder H(object k) { return hold.GetValue(k, delegate(object o) { return new Holder(); }); }

        // guarda o texto original (portugues) e mostra a versao no idioma atual
        static void Tx(object key, string cur, Action<string> set)
        {
            if (string.IsNullOrEmpty(cur)) return;
            Holder h;
            if (!hold.TryGetValue(key, out h))
            {
                string t0 = T(cur);
                if (t0 == cur) return;
                h = H(key); h.Src = cur;
                Put(set, t0);
                return;
            }
            if (h.Src == null || (cur != h.Src && cur != T(h.Src))) h.Src = cur;
            string t = T(h.Src);
            if (t != cur) Put(set, t);
        }

        static void Put(Action<string> set, string t)
        {
            bool was = busy;
            busy = true;
            try { set(t); } finally { busy = was; }
        }

        // acompanha mudancas de texto feitas pelo programa depois (mensagens de status, resultados...)
        static void Hook(Control c)
        {
            Holder h = H(c);
            if (h.Hooked) return;
            h.Hooked = true;
            c.TextChanged += delegate
            {
                if (busy) return;
                string cur = c.Text;
                if (string.IsNullOrEmpty(cur)) return;
                H(c).Src = cur;
                if (Cur == 0) return;
                string t = T(cur);
                if (t != cur) { Put(delegate(string x) { c.Text = x; }, t); if (c is Form) c.Invalidate(); }
            };
        }

        static void ApplyOne(Control c)
        {
            Control cc = c;
            if (c is Label || c is XpButton || c is XpOption || c is Form)
            {
                if (!string.IsNullOrEmpty(c.Text)) Tx(c, c.Text, delegate(string t) { cc.Text = t; if (cc is Form) cc.Invalidate(); });
                Hook(c);
            }
            XpGroup g = c as XpGroup;
            if (g != null && !string.IsNullOrEmpty(g.Title)) Tx(g, g.Title, delegate(string t) { g.Title = t; });
            TextBox tb = c as TextBox;
            if (tb != null && tb.ReadOnly) { if (!string.IsNullOrEmpty(tb.Text)) Tx(tb, tb.Text, delegate(string t) { tb.Text = t; }); Hook(tb); }
            ListView lv = c as ListView;
            if (lv != null) foreach (ColumnHeader h in lv.Columns) { ColumnHeader hh = h; if (!string.IsNullOrEmpty(h.Text)) Tx(h, h.Text, delegate(string t) { hh.Text = t; }); }
            ComboBox cb = c as ComboBox;
            if (cb != null && cb.DropDownStyle == ComboBoxStyle.DropDownList && !(cb.Tag is string && (string)cb.Tag == "nolang") && cb.DrawMode == DrawMode.Normal)
            {
                cb.DrawMode = DrawMode.OwnerDrawFixed;
                cb.ItemHeight = cb.Font.Height + 2;
                cb.DrawItem += DrawCombo;
            }
            if (c.ContextMenuStrip != null) Menu(c.ContextMenuStrip);
        }

        static void DrawCombo(object sender, DrawItemEventArgs e)
        {
            ComboBox cb = (ComboBox)sender;
            e.DrawBackground();
            if (e.Index >= 0 && e.Index < cb.Items.Count)
            {
                object it = cb.Items[e.Index];
                string s = it == null ? "" : T(it.ToString());
                Color fc = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0 ? SystemColors.HighlightText : cb.ForeColor;
                TextRenderer.DrawText(e.Graphics, s, e.Font, new Rectangle(e.Bounds.X + 1, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height), fc,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            e.DrawFocusRectangle();
        }

        // menus de contexto (itens traduzidos; fica registrado para trocar de idioma ao vivo)
        public static void Menu(ToolStrip m)
        {
            if (m == null) return;
            Holder h = H(m);
            if (!h.Hooked) { h.Hooked = true; menus.Add(new WeakReference(m)); }
            MenuItems(m.Items);
        }

        static void MenuItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem it in items)
            {
                ToolStripItem ii = it;
                if (!string.IsNullOrEmpty(it.Text)) Tx(it, it.Text, delegate(string t) { ii.Text = t; });
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems) MenuItems(mi.DropDownItems);
            }
        }

        // traduz a janela inteira (e as que forem adicionadas depois)
        public static void Apply(Control c, ToolTip tip)
        {
            if (c == null) return;
            ApplyOne(c);
            if (hookedAdd.Add(c))
            {
                Control cc = c;
                c.Disposed += delegate { hookedAdd.Remove(cc); };
                c.ControlAdded += delegate(object sd, ControlEventArgs e) { Apply(e.Control, null); };
            }
            foreach (Control k in c.Controls) Apply(k, null);
        }
        static HashSet<Control> hookedAdd = new HashSet<Control>();

        // listas (ListView): troca o texto das celulas; roda de tempos em tempos e ao trocar de idioma
        static void SweepLv(ListView lv)
        {
            foreach (ColumnHeader h in lv.Columns) { ColumnHeader hh = h; if (!string.IsNullOrEmpty(h.Text)) Tx(h, h.Text, delegate(string t) { hh.Text = t; }); }
            foreach (ListViewItem it in lv.Items)
                foreach (ListViewItem.ListViewSubItem sub in it.SubItems)
                {
                    ListViewItem.ListViewSubItem ss = sub;
                    if (!string.IsNullOrEmpty(sub.Text)) Tx(sub, sub.Text, delegate(string t) { ss.Text = t; });
                }
        }

        static void Sweep(Control c, bool all)
        {
            ListView lv = c as ListView;
            if (lv != null && (all || lv.Visible)) SweepLv(lv);
            ComboBox cb = c as ComboBox;
            if (cb != null && all) cb.Invalidate();
            foreach (Control k in c.Controls) Sweep(k, all);
        }

        // troca de idioma ao vivo
        public static void Reapply()
        {
            lock (cache) cache.Clear();
            List<Form> fs = new List<Form>();
            foreach (Form f0 in Application.OpenForms) fs.Add(f0);
            foreach (Form f in fs) { Apply(f, null); Sweep(f, true); }
            foreach (LTip t in Tips) t.Retranslate();
            for (int i = menus.Count - 1; i >= 0; i--)
            {
                ToolStrip m = menus[i].Target as ToolStrip;
                if (m == null) menus.RemoveAt(i); else MenuItems(m.Items);
            }
            if (Changed != null) Changed();
        }

        // a cada 0,5 s procura textos novos em listas (so quando esta em ingles)
        public static void StartSweeper()
        {
            if (sweeper != null) return;
            sweeper = new Timer();
            sweeper.Interval = 250;
            sweeper.Tick += delegate
            {
                if (Cur == 0) return;
                List<Form> fs = new List<Form>();
                foreach (Form f0 in Application.OpenForms) fs.Add(f0);
                foreach (Form f in fs)
                {
                    if (!hookedAdd.Contains(f)) Apply(f, null);   // janela simples criada sem passar pela traducao
                    Sweep(f, false);
                }
            };
            sweeper.Start();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            // reinicio apos atualizar: espera o processo antigo terminar
            try
            {
                string[] ca = Environment.GetCommandLineArgs();
                for (int i = 1; i + 1 < ca.Length; i++) if (ca[i] == "--restart") { int pid; if (int.TryParse(ca[i + 1], out pid)) try { Process.GetProcessById(pid).WaitForExit(8000); } catch (Exception) { } }
            }
            catch (Exception) { }
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            Start();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Start()
        {
            try
            {
                Application.SetCompatibleTextRenderingDefault(false);
                Lang.Init();
                Xp.SetTheme(Xp.IdOf(Settings.Get("theme", "luna")));
                Application.EnableVisualStyles();
                try { PawnIo.CheckAtStartup(null); } catch (Exception) { }
                Startup.Mark("tema e PawnIO verificados");
                Startup.ShowSplash();
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                Dlg.Show(ex.ToString(), "BunoTunner - erro fatal");
            }
        }

        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string n = new AssemblyName(args.Name).Name + ".dll";
            using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream(n))
            {
                if (st == null) return null;
                MemoryStream ms = new MemoryStream();
                byte[] buf = new byte[81920];
                int r;
                while ((r = st.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, r);
                return Assembly.Load(ms.ToArray());
            }
        }
    }

    // Paleta completa de um tema (os campos tem os mesmos nomes dos campos estaticos de Xp)
    class Pal
    {
        public Color Face = Color.FromArgb(236, 233, 216), Border = Color.FromArgb(0, 60, 116), GroupLine = Color.FromArgb(208, 208, 191), GroupText = Color.FromArgb(0, 70, 213);
        public Color Sel = Color.FromArgb(49, 106, 197), TextBorder = Color.FromArgb(127, 157, 185);
        public Color PaneTop = Color.FromArgb(123, 162, 231), PaneBot = Color.FromArgb(99, 117, 214), PaneMid = Color.FromArgb(110, 140, 222), PaneText = Color.White;
        public Color GraphBg = Color.Black, GraphGrid = Color.FromArgb(0, 90, 0), GraphText = Color.Lime;
        public Color OptBorder = Color.FromArgb(29, 82, 129), OptMark = Color.FromArgb(33, 161, 33);
        public Color Text = Color.Black, Dim = Color.Gray, Good = Color.FromArgb(0, 100, 0), Bad = Color.Firebrick, Dis = Color.FromArgb(172, 168, 153);
        public Color EditBg = Color.White, HitBg = Color.FromArgb(255, 205, 205);
        public Color BtnA = Color.White, BtnB = Color.FromArgb(214, 208, 197), BtnDownA = Color.FromArgb(205, 201, 190), BtnDownB = Color.FromArgb(226, 223, 214), Hot = Color.FromArgb(255, 196, 60);
        public Color SelA = Color.FromArgb(228, 240, 255), SelB = Color.FromArgb(150, 186, 246);
        public Color OptA = Color.FromArgb(215, 215, 208), OptB = Color.White;
        public Color TrackBg = Color.White, Tick = Color.FromArgb(172, 168, 153), ThumbA = Color.White, ThumbB = Color.FromArgb(214, 208, 197), ThumbLine = Color.FromArgb(106, 141, 185);
        public Color CapA = Color.FromArgb(9, 151, 255), CapB = Color.FromArgb(0, 80, 238), CapFrame = Color.FromArgb(0, 60, 220), CapText = Color.White, CapShadow = Color.FromArgb(10, 24, 131);
        public Color CapBtn1 = Color.FromArgb(100, 160, 255), CapBtn2 = Color.FromArgb(35, 90, 225), CapClose1 = Color.FromArgb(226, 130, 105), CapClose2 = Color.FromArgb(195, 60, 30), CapX = Color.White;
        public Color ProgBg = Color.White, ProgBorder = Color.FromArgb(104, 104, 104), Prog1 = Color.FromArgb(150, 240, 150), Prog2 = Color.FromArgb(36, 180, 48);
        public Color SepDark = Color.FromArgb(70, 90, 190), SepLight = Color.FromArgb(170, 195, 245);
        public Color Hi = Color.White;
        public int Rad = 3;
        public bool Bevel, Square, Dark;
        public Font Ui = new Font("Tahoma", 8.25f), UiBold = new Font("Tahoma", 8.25f, FontStyle.Bold);
        public Font Caption = new Font("Trebuchet MS", 11f, FontStyle.Bold), Big = new Font("Tahoma", 15f, FontStyle.Bold);
        public Font Title = new Font("Trebuchet MS", 14f, FontStyle.Bold | FontStyle.Italic);

        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static Pal Make(int t)
        {
            Pal p = new Pal();
            switch (t)
            {
                case 1: // Frutiger Aero (Windows 7)
                    p.Face = C(246, 249, 252); p.Border = C(112, 112, 112); p.GroupLine = C(200, 216, 232); p.GroupText = C(30, 57, 91);
                    p.Sel = C(51, 153, 255); p.TextBorder = C(171, 173, 179);
                    p.PaneTop = C(200, 228, 250); p.PaneBot = C(112, 178, 224); p.PaneMid = C(150, 200, 238); p.PaneText = C(24, 56, 100);
                    p.GraphBg = C(8, 22, 40); p.GraphGrid = C(28, 70, 110); p.GraphText = C(140, 220, 255);
                    p.OptBorder = C(142, 143, 143); p.OptMark = C(30, 57, 91);
                    p.Dis = C(109, 109, 109); p.Tick = C(172, 168, 153); p.ThumbB = C(206, 216, 232); p.ThumbLine = C(104, 122, 152);
                    p.OptA = C(214, 222, 232); p.SepDark = C(110, 140, 195); p.SepLight = C(220, 240, 255);
                    break;
                case 2: // Dark Mode / AMOLED
                    p.Face = C(14, 14, 16); p.Border = C(90, 90, 100); p.GroupLine = C(60, 60, 66); p.GroupText = C(90, 170, 255);
                    p.Sel = C(38, 110, 200); p.TextBorder = C(70, 70, 78);
                    p.PaneTop = C(24, 24, 30); p.PaneBot = C(6, 6, 10); p.PaneMid = C(15, 15, 20); p.PaneText = C(150, 150, 160);
                    p.GraphBg = C(0, 0, 0); p.GraphGrid = C(40, 40, 46); p.GraphText = C(90, 220, 140);
                    p.OptBorder = C(120, 120, 130); p.OptMark = C(90, 170, 255);
                    p.Text = C(225, 225, 230); p.Dim = C(140, 140, 150); p.Good = C(80, 200, 120); p.Bad = C(255, 95, 95); p.Dis = C(100, 100, 108);
                    p.EditBg = C(24, 24, 28); p.HitBg = C(90, 30, 30);
                    p.BtnA = C(50, 50, 56); p.BtnB = C(28, 28, 32); p.BtnDownA = C(20, 20, 24); p.BtnDownB = C(36, 36, 42); p.Hot = C(90, 170, 255);
                    p.SelA = C(30, 70, 120); p.SelB = C(20, 50, 95); p.OptA = C(50, 50, 56); p.OptB = C(30, 30, 34);
                    p.TrackBg = C(30, 30, 34); p.Tick = C(100, 100, 110); p.ThumbA = C(100, 100, 110); p.ThumbB = C(60, 60, 68); p.ThumbLine = C(150, 150, 165);
                    p.CapA = C(34, 34, 40); p.CapB = C(0, 0, 0); p.CapFrame = C(0, 0, 0); p.CapText = C(235, 235, 240); p.CapShadow = C(0, 0, 0);
                    p.CapBtn1 = C(80, 80, 90); p.CapBtn2 = C(44, 44, 50); p.CapClose1 = C(210, 80, 70); p.CapClose2 = C(150, 30, 30);
                    p.ProgBg = C(30, 30, 34); p.ProgBorder = C(90, 90, 100); p.Prog1 = C(110, 200, 255); p.Prog2 = C(30, 120, 220);
                    p.SepDark = C(0, 0, 0); p.SepLight = C(60, 60, 70);
                    p.Hi = C(190, 196, 210); p.CapX = C(214, 218, 228); p.PaneText = C(170, 176, 190);
                    p.Rad = 4; p.Dark = true;
                    break;
                case 3: // Neon / Cyberpunk
                    p.Face = C(13, 8, 28); p.Border = C(0, 240, 255); p.GroupLine = C(130, 30, 170); p.GroupText = C(255, 60, 220);
                    p.Sel = C(180, 0, 200); p.TextBorder = C(0, 200, 220);
                    p.PaneTop = C(48, 0, 84); p.PaneBot = C(6, 0, 26); p.PaneMid = C(24, 0, 50); p.PaneText = C(0, 240, 255);
                    p.GraphBg = C(4, 0, 12); p.GraphGrid = C(60, 0, 90); p.GraphText = C(0, 255, 200);
                    p.OptBorder = C(0, 240, 255); p.OptMark = C(255, 60, 220);
                    p.Text = C(200, 250, 255); p.Dim = C(150, 130, 200); p.Good = C(60, 255, 150); p.Bad = C(255, 70, 100); p.Dis = C(90, 80, 120);
                    p.EditBg = C(22, 12, 44); p.HitBg = C(100, 0, 60);
                    p.BtnA = C(56, 22, 100); p.BtnB = C(24, 8, 50); p.BtnDownA = C(20, 6, 40); p.BtnDownB = C(64, 22, 108); p.Hot = C(255, 230, 0);
                    p.SelA = C(120, 20, 160); p.SelB = C(70, 0, 110); p.OptA = C(56, 22, 100); p.OptB = C(24, 8, 50);
                    p.TrackBg = C(24, 10, 50); p.Tick = C(150, 60, 200); p.ThumbA = C(255, 90, 230); p.ThumbB = C(140, 20, 170); p.ThumbLine = C(0, 240, 255);
                    p.CapA = C(255, 0, 170); p.CapB = C(60, 0, 140); p.CapFrame = C(0, 230, 255); p.CapText = Color.White; p.CapShadow = C(120, 0, 100);
                    p.CapBtn1 = C(0, 220, 255); p.CapBtn2 = C(0, 110, 200); p.CapClose1 = C(255, 80, 130); p.CapClose2 = C(200, 0, 70);
                    p.ProgBg = C(20, 8, 40); p.ProgBorder = C(0, 240, 255); p.Prog1 = C(0, 255, 220); p.Prog2 = C(255, 0, 200);
                    p.SepDark = C(0, 240, 255); p.SepLight = C(255, 60, 220);
                    p.Hi = C(170, 235, 255); p.CapX = C(200, 250, 255);
                    p.Rad = 2; p.Dark = true;
                    break;
                case 4: // Steampunk
                    p.Face = C(222, 203, 163); p.Border = C(74, 44, 16); p.GroupLine = C(150, 108, 56); p.GroupText = C(110, 52, 8);
                    p.Sel = C(160, 96, 32); p.TextBorder = C(110, 76, 36);
                    p.PaneTop = C(150, 100, 48); p.PaneBot = C(74, 46, 20); p.PaneMid = C(110, 72, 32); p.PaneText = C(255, 226, 160);
                    p.GraphBg = C(28, 20, 10); p.GraphGrid = C(80, 60, 24); p.GraphText = C(240, 190, 80);
                    p.OptBorder = C(74, 44, 16); p.OptMark = C(120, 60, 10);
                    p.Text = C(44, 26, 10); p.Dim = C(110, 88, 58); p.Good = C(30, 95, 35); p.Bad = C(150, 25, 15); p.Dis = C(150, 130, 100);
                    p.EditBg = C(248, 238, 212); p.HitBg = C(235, 170, 140);
                    p.BtnA = C(238, 208, 140); p.BtnB = C(170, 122, 60); p.BtnDownA = C(140, 98, 48); p.BtnDownB = C(190, 150, 90); p.Hot = C(255, 214, 90);
                    p.SelA = C(250, 224, 150); p.SelB = C(200, 150, 70); p.OptA = C(230, 205, 150); p.OptB = C(250, 240, 215);
                    p.TrackBg = C(248, 238, 212); p.Tick = C(110, 76, 36); p.ThumbA = C(240, 210, 140); p.ThumbB = C(160, 112, 52); p.ThumbLine = C(74, 44, 16);
                    p.CapA = C(176, 112, 52); p.CapB = C(82, 46, 16); p.CapFrame = C(58, 34, 12); p.CapText = C(255, 236, 190); p.CapShadow = C(40, 20, 4);
                    p.CapBtn1 = C(230, 190, 110); p.CapBtn2 = C(150, 100, 40); p.CapClose1 = C(200, 90, 50); p.CapClose2 = C(120, 36, 14); p.CapX = C(40, 20, 4);
                    p.ProgBg = C(248, 238, 212); p.ProgBorder = C(74, 44, 16); p.Prog1 = C(230, 180, 70); p.Prog2 = C(150, 90, 20);
                    p.SepDark = C(74, 44, 16); p.SepLight = C(230, 200, 140);
                    // mais cobre / bronze
                    p.Face = C(206, 160, 112); p.GroupLine = C(130, 74, 30); p.GroupText = C(92, 36, 4); p.EditBg = C(238, 214, 174); p.TrackBg = C(238, 214, 174); p.ProgBg = C(238, 214, 174);
                    p.PaneTop = C(205, 118, 58); p.PaneMid = C(150, 82, 36); p.PaneBot = C(86, 40, 16); p.PaneText = C(255, 230, 170);
                    p.BtnA = C(232, 160, 98); p.BtnB = C(166, 90, 40); p.BtnDownA = C(140, 78, 34); p.BtnDownB = C(190, 118, 64); p.Hot = C(255, 190, 110);
                    p.CapA = C(214, 122, 56); p.CapB = C(100, 46, 16); p.CapFrame = C(70, 32, 10); p.CapText = C(255, 232, 190);
                    p.CapBtn1 = C(236, 170, 100); p.CapBtn2 = C(160, 86, 34);
                    p.SelA = C(246, 190, 130); p.SelB = C(196, 118, 56); p.OptA = C(228, 176, 120); p.OptB = C(246, 222, 184);
                    p.ThumbA = C(236, 170, 100); p.ThumbB = C(150, 80, 32); p.Prog1 = C(240, 170, 80); p.Prog2 = C(170, 80, 20);
                    p.Hi = C(255, 232, 186); p.Dim = C(104, 66, 34);
                    p.Rad = 3;
                    break;
                case 5: // Retro / Vintage (Windows 95/98)
                    p.Face = C(192, 192, 192); p.Border = C(0, 0, 0); p.GroupLine = C(128, 128, 128); p.GroupText = C(0, 0, 128);
                    p.Sel = C(0, 0, 128); p.TextBorder = C(128, 128, 128);
                    p.PaneTop = C(0, 128, 128); p.PaneBot = C(0, 128, 128); p.PaneMid = C(0, 128, 128); p.PaneText = Color.White;
                    p.GraphBg = Color.Black; p.GraphGrid = C(0, 100, 0); p.GraphText = Color.Lime;
                    p.OptBorder = Color.Black; p.OptMark = Color.Black;
                    p.Text = Color.Black; p.Dim = C(96, 96, 96); p.Good = C(0, 110, 0); p.Bad = C(190, 0, 0); p.Dis = C(128, 128, 128);
                    p.EditBg = Color.White; p.HitBg = C(255, 190, 190);
                    p.BtnA = C(192, 192, 192); p.BtnB = C(192, 192, 192); p.BtnDownA = C(176, 176, 176); p.BtnDownB = C(176, 176, 176); p.Hot = C(0, 0, 128);
                    p.SelA = C(224, 224, 224); p.SelB = C(208, 208, 208); p.OptA = Color.White; p.OptB = Color.White;
                    p.TrackBg = Color.White; p.Tick = Color.Black; p.ThumbA = C(192, 192, 192); p.ThumbB = C(192, 192, 192); p.ThumbLine = Color.Black;
                    p.CapA = C(0, 0, 128); p.CapB = C(16, 132, 208); p.CapFrame = C(192, 192, 192); p.CapText = Color.White; p.CapShadow = C(0, 0, 128);
                    p.CapBtn1 = C(192, 192, 192); p.CapBtn2 = C(192, 192, 192); p.CapClose1 = C(192, 192, 192); p.CapClose2 = C(192, 192, 192); p.CapX = Color.Black;
                    p.ProgBg = Color.White; p.ProgBorder = C(128, 128, 128); p.Prog1 = C(0, 0, 128); p.Prog2 = C(0, 0, 128);
                    p.SepDark = C(128, 128, 128); p.SepLight = Color.White;
                    p.Rad = 1; p.Bevel = true; p.Square = true;
                    break;
                case 6: // GTA VI (Vice City): por do sol rosa/laranja
                    p.Face = C(58, 20, 84); p.Border = C(255, 110, 150); p.GroupLine = C(255, 120, 160); p.GroupText = C(255, 214, 96);
                    p.Sel = C(220, 60, 130); p.TextBorder = C(160, 74, 128);
                    p.PaneTop = C(255, 110, 60); p.PaneBot = C(90, 20, 110); p.PaneMid = C(180, 60, 110); p.PaneText = C(255, 244, 220);
                    p.GraphBg = C(20, 6, 36); p.GraphGrid = C(84, 32, 96); p.GraphText = C(255, 190, 70);
                    p.OptBorder = C(255, 140, 90); p.OptMark = C(255, 200, 70);
                    p.Text = C(255, 250, 240); p.Dim = C(226, 190, 214); p.Good = C(90, 230, 170); p.Bad = C(255, 96, 116); p.Dis = C(124, 92, 124);
                    p.EditBg = C(34, 10, 54); p.HitBg = C(120, 24, 70);
                    p.BtnA = C(255, 124, 90); p.BtnB = C(190, 40, 120); p.BtnDownA = C(150, 30, 100); p.BtnDownB = C(220, 80, 110); p.Hot = C(255, 214, 90);
                    p.SelA = C(255, 134, 100); p.SelB = C(190, 50, 130); p.OptA = C(90, 38, 112); p.OptB = C(54, 24, 80);
                    p.TrackBg = C(34, 10, 54); p.Tick = C(224, 124, 152); p.ThumbA = C(255, 194, 90); p.ThumbB = C(220, 60, 120); p.ThumbLine = C(255, 244, 220);
                    p.CapA = C(255, 140, 60); p.CapB = C(200, 40, 130); p.CapFrame = C(30, 10, 50); p.CapText = C(255, 246, 230); p.CapShadow = C(90, 10, 60);
                    p.CapBtn1 = C(255, 200, 100); p.CapBtn2 = C(230, 80, 110); p.CapClose1 = C(255, 110, 110); p.CapClose2 = C(190, 30, 60); p.CapX = C(40, 10, 40);
                    p.ProgBg = C(40, 16, 64); p.ProgBorder = C(255, 140, 90); p.Prog1 = C(255, 200, 80); p.Prog2 = C(240, 60, 130);
                    p.SepDark = C(20, 6, 36); p.SepLight = C(150, 70, 130);
                    p.Hi = C(255, 226, 200);
                    p.Rad = 3; p.Dark = true;
                    break;
            }
            return p;
        }
    }

    // ------------------------------------------------------------------
    //  Temas: 0 Luna (XP), 1 Frutiger Aero (Win7), 2 Dark/AMOLED, 3 Neon, 4 Steampunk, 5 Vintage, 6 GTA VI
    // ------------------------------------------------------------------
#pragma warning disable 0649
    static class Xp
    {
        public static readonly string[] ThemeIds = { "luna", "aero", "dark", "neon", "steam", "retro", "gta6" };
        public static readonly string[] ThemeNames = {
            "Luna (XP)", "Frutiger Aero (Win 7)", "Dark / AMOLED",
            "Neon / Cyberpunk", "Steampunk", "Vintage (Win 95)", "GTA VI (Vice City)" };
        public static int Theme;
        public static bool Aero, Bevel, Square, Dark;
        public static int Rad = 3;
        public static Color Face, Border, GroupLine, GroupText, Sel, TextBorder, PaneTop, PaneBot, PaneMid, PaneText;
        public static Color GraphBg, GraphGrid, GraphText, OptBorder, OptMark;
        public static Color Text, Dim, Good, Bad, Dis, EditBg, HitBg, Hi;
        public static Brush HiBr = Brushes.White;
        public static Color BtnA, BtnB, BtnDownA, BtnDownB, Hot, SelA, SelB, OptA, OptB;
        public static Color TrackBg, Tick, ThumbA, ThumbB, ThumbLine;
        public static Color CapA, CapB, CapFrame, CapText, CapShadow, CapBtn1, CapBtn2, CapClose1, CapClose2, CapX;
        public static Color ProgBg, ProgBorder, Prog1, Prog2, SepDark, SepLight;
        public static Font Ui, UiBold, Caption, Big, Title;
        static Font pUi, pBold, pBig, pCap, pTitle;
        static Color pFace;
        static Pal[] pals;
        public static List<Form> Forms = new List<Form>();

        static Xp() { SetTheme(0); }

        public static int IdOf(string id)
        {
            for (int i = 0; i < ThemeIds.Length; i++) if (ThemeIds[i] == id) return i;
            return 0;
        }

        public static void SetTheme(int t)
        {
            if (t < 0 || t >= ThemeIds.Length) t = 0;
            pUi = Ui; pBold = UiBold; pBig = Big; pCap = Caption; pTitle = Title; pFace = Face;
            if (pals == null)
            {
                pals = new Pal[ThemeIds.Length];
                for (int i = 0; i < pals.Length; i++) pals[i] = Pal.Make(i);
            }
            Theme = t; Aero = t == 1;
            Pal p = pals[t];
            foreach (FieldInfo f in typeof(Pal).GetFields())
            {
                FieldInfo d = typeof(Xp).GetField(f.Name, BindingFlags.Public | BindingFlags.Static);
                if (d != null) d.SetValue(null, f.GetValue(p));
            }
            HiBr = new SolidBrush(Hi);
        }

        // Cores "de papel" (texto, destaque, verde, vermelho, cinza) trocam junto com o tema
        static Color Remap(Color c)
        {
            if (c.A == 0 || pals == null) return c;
            int a = c.ToArgb();
            foreach (Pal p in pals)
            {
                if (p.Text.ToArgb() == a) return Text;
                if (p.GroupText.ToArgb() == a) return GroupText;
                if (p.Good.ToArgb() == a) return Good;
                if (p.Bad.ToArgb() == a) return Bad;
                if (p.Dim.ToArgb() == a) return Dim;
            }
            return c;
        }

        // Aplica fontes/cores do tema atual a uma janela e a todos os controles dela
        public static void Apply(Control c)
        {
            if (c.Font == pUi) c.Font = Ui;
            else if (c.Font == pBold) c.Font = UiBold;
            else if (c.Font == pBig) c.Font = Big;
            else if (c.Font == pCap) c.Font = Caption;
            if (c is Form) c.BackColor = Face;
            else if (c is TaskPane) c.BackColor = PaneMid;
            else if (c is XpText) c.BackColor = TextBorder;
            else if (c is ListView || c is ListBox || c is ComboBox || c is TextBoxBase)
            {
                c.BackColor = EditBg; c.ForeColor = Text;
                ComboBox cb = c as ComboBox;
                if (cb != null) cb.FlatStyle = (Dark || Bevel) ? FlatStyle.Flat : FlatStyle.Standard;
                ListView lv = c as ListView;
                if (lv != null)
                    foreach (ListViewItem it in lv.Items)
                    {
                        Color n = Remap(it.ForeColor);
                        if (n != it.ForeColor) it.ForeColor = n;
                    }
            }
            else if (c is Label && (c.Tag as string) == "warn") { c.BackColor = EditBg; c.ForeColor = Text; }
            else if (c.BackColor.A != 0 && c.BackColor.ToArgb() == pFace.ToArgb()) c.BackColor = Face;
            if (!(c is ListView || c is ListBox || c is ComboBox || c is TextBoxBase) && c.ForeColor.A != 0)
            {
                Color n = Remap(c.ForeColor);
                if (n != c.ForeColor) c.ForeColor = n;
            }
            if (c is XpGroup || c is TaskPane || c.GetType() == typeof(Panel)) Fx.Hook(c);
            foreach (Control k in c.Controls) Apply(k);
            XpText xt = c as XpText;
            if (xt != null) xt.Height = xt.Box.PreferredHeight + 4;
            c.Invalidate();
        }

        public static void ApplyAll()
        {
            foreach (Form f in new List<Form>(Forms))
            {
                Apply(f);
                XpForm xf = f as XpForm;
                if (xf != null) xf.RefreshFrame();
            }
        }

        // Relevo estilo Windows 95 (retro)
        public static void Bevel3D(Graphics g, Rectangle r, bool down, Color? fill)
        {
            if (fill.HasValue) using (SolidBrush b = new SolidBrush(fill.Value)) g.FillRectangle(b, r);
            Color hi = Color.White, lo = Color.FromArgb(64, 64, 64), mid = Color.FromArgb(128, 128, 128), lite = Color.FromArgb(223, 223, 223);
            if (down) { Color t = hi; hi = lo; lo = t; t = lite; lite = mid; mid = t; }
            int x0 = r.X, y0 = r.Y, x1 = r.Right - 1, y1 = r.Bottom - 1;
            using (Pen p1 = new Pen(hi)) { g.DrawLine(p1, x0, y0, x1 - 1, y0); g.DrawLine(p1, x0, y0, x0, y1 - 1); }
            using (Pen p2 = new Pen(lo)) { g.DrawLine(p2, x0, y1, x1, y1); g.DrawLine(p2, x1, y0, x1, y1); }
            using (Pen p3 = new Pen(lite)) { g.DrawLine(p3, x0 + 1, y0 + 1, x1 - 2, y0 + 1); g.DrawLine(p3, x0 + 1, y0 + 1, x0 + 1, y1 - 2); }
            using (Pen p4 = new Pen(mid)) { g.DrawLine(p4, x0 + 1, y1 - 1, x1 - 1, y1 - 1); g.DrawLine(p4, x1 - 1, y0 + 1, x1 - 1, y1 - 1); }
        }
        // Botao "vidro" do Windows 7: duas metades com degrade + brilho interno
        public static void Glass(Graphics g, Rectangle r, int rad, Color t1, Color t2, Color b1, Color b2, Color border)
        {
            int h2 = Math.Max(2, r.Height / 2);
            using (GraphicsPath p = Round(r, rad))
            {
                g.SetClip(p);
                using (LinearGradientBrush u = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, h2), t1, t2, 90f))
                    g.FillRectangle(u, r.X, r.Y, r.Width, h2);
                using (LinearGradientBrush l = new LinearGradientBrush(new Rectangle(r.X, r.Y + h2, r.Width, Math.Max(2, r.Height - h2)), b1, b2, 90f))
                    g.FillRectangle(l, r.X, r.Y + h2, r.Width, r.Height - h2);
                g.ResetClip();
                using (Pen bp = new Pen(border)) g.DrawPath(bp, p);
            }
            using (GraphicsPath ip = Round(Rectangle.Inflate(r, -1, -1), Math.Max(1, rad - 1)))
            using (Pen wp = new Pen(Color.FromArgb(140, 255, 255, 255)))
                g.DrawPath(wp, ip);
        }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            int d = rad * 2, x = r.X, y = r.Y, w = r.Width - 1, h = r.Height - 1;
            GraphicsPath p = new GraphicsPath();
            if (rad <= 0 || r.Width < d + 1 || r.Height < d + 1) { p.AddRectangle(new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h))); return p; }
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Logo: esfera dourada 3D em pixel art (grade 20x20, ampliada sem suavizar)
        public const string Version = "v1.44.1";
        public const int LogoGrid = 20;
        public static Bitmap Logo(int scale)
        {
            int N = LogoGrid;
            Bitmap px = new Bitmap(N, N, PixelFormat.Format32bppArgb);
            double c = (N - 1) / 2.0, R = N / 2.0 - 0.3;
            double lx = -0.5, ly = -0.6, lz = 0.62, ln = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            lx /= ln; ly /= ln; lz /= ln;
            int[,] bayer = { { 0, 2 }, { 3, 1 } };
            Color outline = Color.FromArgb(92, 52, 0);
            Color[] tone = {
                Color.FromArgb(140, 84, 0), Color.FromArgb(184, 116, 4), Color.FromArgb(222, 152, 12),
                Color.FromArgb(248, 190, 36), Color.FromArgb(255, 220, 84), Color.FromArgb(255, 247, 186) };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    if (!InSphere(x, y, c, R)) continue;
                    bool edge = !InSphere(x - 1, y, c, R) || !InSphere(x + 1, y, c, R) || !InSphere(x, y - 1, c, R) || !InSphere(x, y + 1, c, R);
                    if (edge) { px.SetPixel(x, y, outline); continue; }
                    double dx = (x - c) / R, dy = (y - c) / R;
                    double dz = Math.Sqrt(Math.Max(0, 1 - dx * dx - dy * dy));
                    double lam = dx * lx + dy * ly + dz * lz;
                    // luz refletida suave embaixo/direita para dar volume
                    lam += 0.10 * Math.Max(0, dx * 0.6 + dy * 0.7) * (1 - dz);
                    lam += (bayer[y % 2, x % 2] - 1.5) * 0.035;
                    int t = lam < 0.12 ? 0 : lam < 0.34 ? 1 : lam < 0.56 ? 2 : lam < 0.76 ? 3 : lam < 0.90 ? 4 : 5;
                    px.SetPixel(x, y, tone[t]);
                }
            // brilho especular em pixels
            Color hi = Color.FromArgb(255, 255, 235);
            px.SetPixel(5, 5, hi); px.SetPixel(6, 5, hi); px.SetPixel(5, 6, hi);
            px.SetPixel(7, 4, tone[5]); px.SetPixel(4, 7, tone[5]);

            Bitmap b = new Bitmap(N * scale, N * scale);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(px, new Rectangle(0, 0, N * scale, N * scale), 0, 0, N, N, GraphicsUnit.Pixel);
            }
            px.Dispose();
            return b;
        }
        static bool InSphere(int x, int y, double c, double R)
        {
            double dx = x - c, dy = y - c;
            return dx * dx + dy * dy <= R * R;
        }
    }

#pragma warning restore 0649

    // ------------------------------------------------------------------
    //  Controles desenhados no estilo XP
    // ------------------------------------------------------------------
    class XpButton : Control
    {
        bool hot, down;
        public XpButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Font = Xp.Ui; Size = new Size(90, 24); TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            if (Xp.Aero)
            {
                Color t1, t2, b1, b2, bd;
                if (!Enabled) { t1 = Color.FromArgb(244, 244, 244); t2 = Color.FromArgb(240, 240, 240); b1 = Color.FromArgb(235, 235, 235); b2 = Color.FromArgb(230, 230, 230); bd = Color.FromArgb(173, 178, 181); }
                else if (down) { t1 = Color.FromArgb(196, 229, 246); t2 = Color.FromArgb(172, 219, 243); b1 = Color.FromArgb(146, 206, 237); b2 = Color.FromArgb(104, 177, 225); bd = Color.FromArgb(44, 98, 139); }
                else if (hot) { t1 = Color.FromArgb(234, 246, 253); t2 = Color.FromArgb(217, 240, 252); b1 = Color.FromArgb(190, 230, 253); b2 = Color.FromArgb(167, 217, 245); bd = Color.FromArgb(60, 127, 177); }
                else { t1 = Color.FromArgb(242, 242, 242); t2 = Color.FromArgb(235, 235, 235); b1 = Color.FromArgb(221, 221, 221); b2 = Color.FromArgb(207, 207, 207); bd = Color.FromArgb(112, 112, 112); }
                Xp.Glass(g, r, 3, t1, t2, b1, b2, bd);
                TextRenderer.DrawText(g, Text, Font, r, Enabled ? Color.Black : Xp.Dis,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
            if (Xp.Bevel)
            {
                Xp.Bevel3D(g, r, down, down ? Xp.BtnDownA : Xp.BtnA);
                if (Focused && Enabled) using (Pen fp = new Pen(Color.Black) { DashStyle = DashStyle.Dot }) g.DrawRectangle(fp, 4, 4, Width - 9, Height - 9);
                Rectangle tr = down ? new Rectangle(1, 1, Width, Height) : r;
                TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Xp.Text : Xp.Dis, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
            Color c1 = down ? Xp.BtnDownA : Xp.BtnA;
            Color c2 = down ? Xp.BtnDownB : Xp.BtnB;
            using (GraphicsPath p = Xp.Round(r, Xp.Rad))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(r, c1, c2, 90f)) g.FillPath(br, p);
                using (Pen pen = new Pen(Enabled ? Xp.Border : Xp.Dis)) g.DrawPath(pen, p);
            }
            if (hot && Enabled && !down)
            {
                using (GraphicsPath ip = Xp.Round(Rectangle.Inflate(r, -2, -2), 2))
                using (Pen hp = new Pen(Xp.Hot, 2f))
                    g.DrawPath(hp, ip);
            }
            TextRenderer.DrawText(g, Text, Font, r, Enabled ? Xp.Text : Xp.Dis,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Radio / CheckBox estilo XP
    class XpOption : Control
    {
        bool chk, hot;
        public bool Radio;
        public event EventHandler CheckedChanged;
        public XpOption()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = Xp.Ui; Size = new Size(200, 18);
            MouseEnter += delegate { hot = true; Invalidate(); };
            MouseLeave += delegate { hot = false; Invalidate(); };
        }
        public bool Checked
        {
            get { return chk; }
            set
            {
                if (chk == value) return;
                chk = value;
                if (value && Radio && Parent != null)
                    foreach (Control c in Parent.Controls)
                    {
                        XpOption o = c as XpOption;
                        if (o != null && o != this && o.Radio) o.Checked = false;
                    }
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }
        protected override void OnClick(EventArgs e) { if (Radio) Checked = true; else Checked = !Checked; base.OnClick(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            int y = (Height - 13) / 2;
            Rectangle b = new Rectangle(1, y, 12, 12);
            using (LinearGradientBrush br = new LinearGradientBrush(b, (Xp.Aero && hot) ? Color.FromArgb(190, 230, 253) : Xp.OptA, (Xp.Aero && hot) ? Color.FromArgb(234, 246, 253) : Xp.OptB, 45f))
            {
                if (Radio) g.FillEllipse(br, b); else g.FillRectangle(br, b);
            }
            using (Pen p = new Pen((Xp.Aero && hot) ? Color.FromArgb(60, 127, 177) : Xp.OptBorder))
            {
                if (Radio) g.DrawEllipse(p, b); else g.DrawRectangle(p, b);
            }
            if (chk)
            {
                Color green = Xp.OptMark;
                if (Radio)
                {
                    using (SolidBrush gb = new SolidBrush(green)) g.FillEllipse(gb, b.X + 3, b.Y + 3, 6, 6);
                }
                else
                {
                    using (Pen cp = new Pen(green, 2f))
                        g.DrawLines(cp, new Point[] { new Point(b.X + 2, b.Y + 6), new Point(b.X + 5, b.Y + 9), new Point(b.X + 10, b.Y + 3) });
                }
            }
            TextRenderer.DrawText(g, Text, Font, new Point(19, (Height - Font.Height) / 2), Enabled ? Xp.Text : Xp.Dis);
        }
    }

    // Slider (trackbar) estilo XP
    class XpSlider : Control
    {
        const int PAD = 8;
        int min = 0, max = 100, val = 0;
        bool drag;
        public event EventHandler ValueChanged;
        public XpSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable, true);
            Height = 34; Width = 300; TabStop = true;
        }
        public int Minimum { get { return min; } set { min = value; if (val < min) val = min; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; if (val > max) val = max; Invalidate(); } }
        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == val) return;
                val = v; Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        void FromX(int x)
        {
            double f = (x - PAD) / (double)Math.Max(1, Width - 2 * PAD);
            Value = min + (int)Math.Round(f * (max - min));
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Enabled) { drag = true; Focus(); FromX(e.X); } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) FromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Invalidate(); base.OnMouseUp(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value = val - 1;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value = val + 1;
            else if (e.KeyCode == Keys.PageDown) Value = val - 10;
            else if (e.KeyCode == Keys.PageUp) Value = val + 10;
            base.OnKeyDown(e);
        }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            int cy = Height / 2, x0 = PAD, x1 = Width - PAD;
            using (SolidBrush tb = new SolidBrush(Xp.TrackBg)) g.FillRectangle(tb, x0, cy - 2, x1 - x0, 4);
            using (Pen d = new Pen(Xp.Tick))
            {
                g.DrawRectangle(d, x0, cy - 2, x1 - x0, 4);
                for (int i = 0; i <= 10; i++)
                {
                    int x = x0 + (x1 - x0) * i / 10;
                    g.DrawLine(d, x, cy + 11, x, cy + 14);
                }
            }
            float px = x0 + (x1 - x0) * (float)(val - min) / Math.Max(1, max - min);
            PointF[] poly = {
                new PointF(px - 5, cy - 11), new PointF(px + 5, cy - 11), new PointF(px + 5, cy + 4),
                new PointF(px, cy + 10), new PointF(px - 5, cy + 4) };
            Color a = drag ? Color.FromArgb(200, 222, 255) : Xp.ThumbA;
            Color b = drag ? Color.FromArgb(140, 180, 240) : Xp.ThumbB;
            if (!Enabled) { a = Xp.BtnDownB; b = Xp.BtnDownA; }
            using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(px - 5, cy - 11, 10, 21), a, b, 90f))
                g.FillPolygon(br, poly);
            using (Pen p = new Pen(Enabled ? Xp.ThumbLine : Xp.Dis))
                g.DrawPolygon(p, poly);
        }
    }

    // Caixa de texto com borda azul-acinzentada do XP
    class XpText : Panel
    {
        public TextBox Box = new TextBox();
        public XpText()
        {
            BackColor = Xp.TextBorder;
            Padding = new Padding(1);
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Xp.Ui;
            Box.BackColor = Xp.EditBg; Box.ForeColor = Xp.Text;
            Box.Dock = DockStyle.Fill;
            Controls.Add(Box);
            Box.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.A) { Box.SelectAll(); e.SuppressKeyPress = true; e.Handled = true; }
            };
            Height = Box.PreferredHeight + 4;
            Width = 60;
        }
        public override string Text { get { return Box.Text; } set { Box.Text = value; } }
        public bool ReadOnly { get { return Box.ReadOnly; } set { Box.ReadOnly = value; } }
    }

    // Caixa de grupo estilo XP
    class XpGroup : Panel
    {
        string title = "";
        public string Title { get { return title; } set { title = value; Invalidate(); } }
        public XpGroup() { DoubleBuffered = true; BackColor = Xp.Face; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Fx.Draw(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Xp.Theme == 4) Fx.GroupPipe(g, Width, Height);
            else using (GraphicsPath p = Xp.Round(new Rectangle(0, 8, Width, Height - 8), Xp.Aero ? 6 : 4))
            using (Pen pen = new Pen(Xp.GroupLine))
                g.DrawPath(pen, p);
            if (Xp.Aero)
                using (GraphicsPath p2 = Xp.Round(new Rectangle(1, 9, Width - 2, Height - 10), 5))
                using (Pen wp = new Pen(Color.FromArgb(210, 255, 255, 255)))
                    g.DrawPath(wp, p2);
            Size sz = TextRenderer.MeasureText(title, Xp.UiBold);
            using (SolidBrush b = new SolidBrush(BackColor)) g.FillRectangle(b, 9, 0, sz.Width + 2, 16);
            TextRenderer.DrawText(g, title, Xp.UiBold, new Point(9, 1), Xp.GroupText);
        }
    }

    // Painel lateral (azul Luna ou vidro Aero) - so icones
    class TaskPane : Panel
    {
        static void Bubble(Graphics g, int x, int y, int d)
        {
            using (SolidBrush f = new SolidBrush(Color.FromArgb(38, 255, 255, 255))) g.FillEllipse(f, x, y, d, d);
            using (Pen p = new Pen(Color.FromArgb(150, 255, 255, 255), 1.2f)) g.DrawEllipse(p, x, y, d, d);
            using (SolidBrush h = new SolidBrush(Color.FromArgb(170, 255, 255, 255))) g.FillEllipse(h, x + d * 0.2f, y + d * 0.14f, d * 0.34f, d * 0.22f);
        }
        public TaskPane() { DoubleBuffered = true; BackColor = Xp.PaneMid; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush br = new LinearGradientBrush(ClientRectangle, Xp.PaneTop, Xp.PaneBot, 90f))
                g.FillRectangle(br, ClientRectangle);
            Fx.Draw(this, g);
            if (Xp.Aero)
            {
                // fundo azul/aqua com reflexo de vidro e bolhas (Frutiger Aero)
                using (LinearGradientBrush ag = new LinearGradientBrush(ClientRectangle, Color.White, Color.White, 90f))
                {
                    ColorBlend cb = new ColorBlend(4);
                    cb.Positions = new float[] { 0f, 0.35f, 0.75f, 1f };
                    cb.Colors = new Color[] { Color.FromArgb(216, 238, 253), Color.FromArgb(152, 207, 241), Color.FromArgb(106, 192, 224), Color.FromArgb(122, 208, 192) };
                    ag.InterpolationColors = cb;
                    g.FillRectangle(ag, ClientRectangle);
                }
                using (GraphicsPath gl = new GraphicsPath())
                {
                    gl.AddEllipse(-Width, -Height / 2, Width * 3, Height);
                    using (LinearGradientBrush gb = new LinearGradientBrush(new Rectangle(0, 0, Width, Height / 2 + 2), Color.FromArgb(125, 255, 255, 255), Color.FromArgb(8, 255, 255, 255), 90f))
                        g.FillPath(gb, gl);
                }
                Bubble(g, -22, Height - 150, 112);
                Bubble(g, 30, Height - 70, 40);
                Bubble(g, 40, 430, 24);
                Bubble(g, 6, 190, 18);
                using (Pen edge = new Pen(Color.FromArgb(150, 110, 160, 210))) g.DrawLine(edge, Width - 1, 0, Width - 1, Height);
            }
            if (Xp.Theme == 4) { Fx.HPipe(g, 6, Width - 6, 338, 7); Fx.VPipe(g, Width - 3, 0, Height, 5); }
            else using (Pen dark = new Pen(Xp.SepDark))
            using (Pen light = new Pen(Xp.SepLight))
            {
                g.DrawLine(dark, 10, 338, Width - 10, 338);
                g.DrawLine(light, 10, 339, Width - 10, 339);
            }
            using (Font vf = new Font("Tahoma", 7f))
                TextRenderer.DrawText(g, Xp.Version, vf, new Rectangle(0, Height - 24, Width, 16), Xp.PaneText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Botao quadrado so com icone (desenhado por codigo)
    class XpIconButton : Control
    {
        public string Kind;
        public bool Selected;
        public int Badge;   // 1 = play, 2 = parar (selo no canto)
        bool hot, down;
        public XpIconButton(string kind, int size)
        {
            Kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            Size = new Size(size, size);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            if (Xp.Aero)
            {
                Color t1 = Color.FromArgb(246, 249, 253), t2 = Color.FromArgb(234, 240, 248), b1 = Color.FromArgb(214, 224, 238), b2 = Color.FromArgb(192, 206, 226), bd = Color.FromArgb(128, 144, 172);
                if (Selected) { t1 = Color.FromArgb(206, 230, 252); t2 = Color.FromArgb(176, 212, 246); b1 = Color.FromArgb(132, 188, 238); b2 = Color.FromArgb(96, 164, 226); bd = Color.FromArgb(40, 92, 152); }
                else if (hot) { t1 = Color.FromArgb(238, 249, 255); t2 = Color.FromArgb(220, 242, 253); b1 = Color.FromArgb(190, 232, 253); b2 = Color.FromArgb(166, 218, 246); bd = Color.FromArgb(60, 127, 177); }
                if (down) { t1 = Color.FromArgb(196, 229, 246); t2 = Color.FromArgb(172, 219, 243); b1 = Color.FromArgb(146, 206, 237); b2 = Color.FromArgb(104, 177, 225); bd = Color.FromArgb(44, 98, 139); }
                Xp.Glass(g, r, 5, t1, t2, b1, b2, bd);
            }
            else
            {
                Color c1 = Xp.BtnA, c2 = Xp.BtnB;
                if (Selected) { c1 = Xp.SelA; c2 = Xp.SelB; }
                if (down) { c1 = Xp.BtnDownA; c2 = Xp.BtnDownB; }
                if (Xp.Bevel)
                {
                    Xp.Bevel3D(g, r, down || Selected, Selected && !down ? Color.FromArgb(224, 224, 224) : c1);
                }
                else
                {
                    using (GraphicsPath p = Xp.Round(r, Xp.Rad + 2))
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(r, c1, c2, 90f)) g.FillPath(br, p);
                        using (Pen pen = new Pen(Selected ? Xp.Sel : Xp.Border, Selected ? 2f : 1f)) g.DrawPath(pen, p);
                    }
                    if (hot && !down)
                        using (GraphicsPath ip = Xp.Round(Rectangle.Inflate(r, -2, -2), 4))
                        using (Pen hp = new Pen(Xp.Hot, 2f))
                            g.DrawPath(hp, ip);
                }
            }
            int pad = Math.Max(3, Width / 6);
            Draw(g, Kind, Rectangle.Inflate(r, -pad, -pad));
            if (Badge != 0)
            {
                float bs = Math.Max(16, Width * 0.42f);
                RectangleF br = new RectangleF(Width - bs - 1, Height - bs - 1, bs, bs);
                using (SolidBrush bb = new SolidBrush(Badge == 1 ? Color.FromArgb(34, 160, 60) : Color.FromArgb(200, 40, 40))) g.FillEllipse(bb, br);
                using (Pen bp = new Pen(Color.White, 1.6f)) g.DrawEllipse(bp, br);
                if (Badge == 1)
                    g.FillPolygon(Brushes.White, new PointF[] { new PointF(br.X + bs * 0.38f, br.Y + bs * 0.27f), new PointF(br.X + bs * 0.38f, br.Y + bs * 0.73f), new PointF(br.X + bs * 0.74f, br.Y + bs * 0.5f) });
                else
                    g.FillRectangle(Brushes.White, br.X + bs * 0.3f, br.Y + bs * 0.3f, bs * 0.4f, bs * 0.4f);
            }
        }

        // ponto da bandeira ondulada (u, v em 0..1)
        static PointF FlagPt(Rectangle ic, float u, float v)
        {
            float w = ic.Width, h = ic.Height;
            float wave = (float)Math.Sin(u * Math.PI * 1.7 + v * 0.6) * h * 0.07f;
            return new PointF(ic.X + w * (0.04f + 0.92f * u) + (v - 0.5f) * w * 0.06f, ic.Y + h * (0.10f + 0.78f * v) + wave);
        }
        // pontos de Bezier da chama (inner = 0 externa, 1 interna menor)
        static PointF[] FlamePts(Rectangle ic, float inner)
        {
            float[] c = { 0.50f, 0.98f,  0.12f, 0.98f,  0.06f, 0.62f,  0.28f, 0.40f,  0.30f, 0.55f,  0.38f, 0.58f,  0.42f, 0.50f,
                          0.40f, 0.30f,  0.50f, 0.12f,  0.58f, 0.00f,  0.62f, 0.22f,  0.82f, 0.30f,  0.88f, 0.58f,  0.94f, 0.84f,  0.78f, 0.98f };
            PointF[] r = new PointF[15];
            // ordem: P0, c1, c2, P1, c3, c4, P2, c5, c6, P3, c7, c8, P4, c9, c10 (o ultimo segmento fecha em P0)
            float k = inner > 0 ? 0.52f : 1f, ox = 0.5f, oy = 0.98f;
            for (int i = 0; i < 15; i++)
            {
                float x = c[i * 2], y = c[i * 2 + 1];
                if (inner > 0) { x = ox + (x - ox) * k; y = oy + (y - oy) * k - 0.02f; }
                r[i] = new PointF(ic.X + x * ic.Width, ic.Y + y * ic.Height);
            }
            return r;
        }

        static void Draw(Graphics g, string kind, Rectangle ic)
        {
            float w = ic.Width, h = ic.Height, cx = ic.X + w / 2f, cy = ic.Y + h / 2f, rad = Math.Min(w, h) / 2f;
            switch (kind)
            {
                case "update":
                    {
                        // seta para baixo sobre uma bandeja: baixar/atualizar
                        using (Pen sh = new Pen(Color.FromArgb(0, 100, 200), Math.Max(2.4f, w * 0.18f))) { sh.StartCap = LineCap.Round; sh.EndCap = LineCap.Round; g.DrawLine(sh, cx, ic.Y + h * 0.06f, cx, ic.Y + h * 0.56f); }
                        using (SolidBrush hd = new SolidBrush(Color.FromArgb(30, 140, 235)))
                            g.FillPolygon(hd, new PointF[] { new PointF(ic.X + w * 0.12f, ic.Y + h * 0.46f), new PointF(ic.Right - w * 0.12f, ic.Y + h * 0.46f), new PointF(cx, ic.Y + h * 0.82f) });
                        using (Pen tr = new Pen(Color.FromArgb(0, 100, 200), Math.Max(2f, w * 0.14f))) { tr.StartCap = LineCap.Round; tr.EndCap = LineCap.Round; g.DrawLine(tr, ic.X + w * 0.04f, ic.Bottom - h * 0.04f, ic.Right - w * 0.04f, ic.Bottom - h * 0.04f); }
                        break;
                    }
                case "win10":
                case "win10on":
                    {
                        // logotipo do Windows 10: quatro paineis (azul = ligado, cinza = desligado)
                        bool on = kind == "win10on";
                        Color c1 = on ? Color.FromArgb(70, 170, 255) : Color.FromArgb(185, 190, 200), c2 = on ? Color.FromArgb(0, 105, 200) : Color.FromArgb(120, 126, 138);
                        PointF[][] panes = {
                            new PointF[] { new PointF(0f, 0.14f), new PointF(0.47f, 0.07f), new PointF(0.47f, 0.47f), new PointF(0f, 0.47f) },
                            new PointF[] { new PointF(0.53f, 0.06f), new PointF(1f, 0f), new PointF(1f, 0.47f), new PointF(0.53f, 0.47f) },
                            new PointF[] { new PointF(0f, 0.53f), new PointF(0.47f, 0.53f), new PointF(0.47f, 0.93f), new PointF(0f, 0.86f) },
                            new PointF[] { new PointF(0.53f, 0.53f), new PointF(1f, 0.53f), new PointF(1f, 1f), new PointF(0.53f, 0.94f) } };
                        RectangleF wb = new RectangleF(ic.X, ic.Y, w, h);
                        using (LinearGradientBrush wg = new LinearGradientBrush(wb, c1, c2, 90f))
                            foreach (PointF[] pn in panes)
                            {
                                PointF[] q = new PointF[4];
                                for (int i = 0; i < 4; i++) q[i] = new PointF(ic.X + pn[i].X * w, ic.Y + pn[i].Y * h);
                                g.FillPolygon(wg, q);
                            }
                        break;
                    }
                case "fan":
                    {
                        Rectangle fb = new Rectangle(ic.X - 1, ic.Y - 1, ic.Width + 2, ic.Height + 2);
                        using (GraphicsPath fp = Xp.Round(fb, 4))
                        {
                            using (LinearGradientBrush fg = new LinearGradientBrush(fb, Color.FromArgb(100, 112, 132), Color.FromArgb(34, 42, 56), 90f)) g.FillPath(fg, fp);
                            using (Pen fpn = new Pen(Color.FromArgb(16, 22, 32))) g.DrawPath(fpn, fp);
                        }
                        using (SolidBrush screw = new SolidBrush(Color.FromArgb(205, 214, 228)))
                        {
                            g.FillEllipse(screw, fb.X + 1.5f, fb.Y + 1.5f, 2.6f, 2.6f);
                            g.FillEllipse(screw, fb.Right - 4.2f, fb.Y + 1.5f, 2.6f, 2.6f);
                            g.FillEllipse(screw, fb.X + 1.5f, fb.Bottom - 4.2f, 2.6f, 2.6f);
                            g.FillEllipse(screw, fb.Right - 4.2f, fb.Bottom - 4.2f, 2.6f, 2.6f);
                        }
                        float r0 = rad * 0.9f;
                        using (SolidBrush hole = new SolidBrush(Color.FromArgb(12, 18, 30))) g.FillEllipse(hole, cx - r0, cy - r0, r0 * 2, r0 * 2);
                        using (Pen ring = new Pen(Color.FromArgb(150, 195, 240), 1.2f)) g.DrawEllipse(ring, cx - r0, cy - r0, r0 * 2, r0 * 2);
                        for (int i = 0; i < 7; i++)
                        {
                            GraphicsState st = g.Save();
                            g.TranslateTransform(cx, cy);
                            g.RotateTransform(i * 360f / 7f);
                            using (GraphicsPath bl = new GraphicsPath())
                            {
                                float hb = r0 * 0.16f, rr = r0 * 0.92f;
                                bl.AddBezier(0, -hb, rr * 0.55f, -rr * 0.2f, rr * 0.6f, -rr * 0.7f, rr * 0.14f, -rr);
                                bl.AddBezier(rr * 0.14f, -rr, -rr * 0.2f, -rr * 0.72f, -rr * 0.16f, -rr * 0.3f, 0, -hb);
                                bl.CloseFigure();
                                using (LinearGradientBrush bb = new LinearGradientBrush(new RectangleF(-rr * 0.3f, -rr, rr * 0.9f, rr), Color.FromArgb(160, 215, 255), Color.FromArgb(20, 90, 210), 60f)) g.FillPath(bb, bl);
                                using (Pen bp = new Pen(Color.FromArgb(10, 40, 110), 0.8f)) g.DrawPath(bp, bl);
                            }
                            g.Restore(st);
                        }
                        RectangleF hub = new RectangleF(cx - r0 * 0.24f, cy - r0 * 0.24f, r0 * 0.48f, r0 * 0.48f);
                        using (LinearGradientBrush hg = new LinearGradientBrush(hub, Color.FromArgb(255, 238, 160), Color.FromArgb(200, 130, 10), 90f)) g.FillEllipse(hg, hub);
                        g.FillEllipse(Xp.HiBr, cx - r0 * 0.1f, cy - r0 * 0.14f, r0 * 0.12f, r0 * 0.1f);
                        break;
                    }
                case "oc":
                    {
                        // display digital de clock: "2850 MHz +"
                        RectangleF scr = new RectangleF(ic.X - 1, ic.Y + h * 0.16f, w + 2, h * 0.68f);
                        using (GraphicsPath sp = Xp.Round(Rectangle.Round(scr), 3))
                        {
                            using (LinearGradientBrush sg = new LinearGradientBrush(scr, Color.FromArgb(74, 84, 104), Color.FromArgb(27, 33, 48), 90f)) g.FillPath(sg, sp);
                            using (Pen so = new Pen(Color.FromArgb(15, 20, 32), 1.4f)) g.DrawPath(so, sp);
                        }
                        RectangleF lcd = new RectangleF(scr.X + w * 0.09f, scr.Y + scr.Height * 0.14f, scr.Width - w * 0.18f, scr.Height * 0.72f);
                        using (SolidBrush lb = new SolidBrush(Color.FromArgb(7, 34, 15))) g.FillRectangle(lb, lcd);
                        using (Font df = new Font("Consolas", Math.Max(7f, lcd.Height * 0.52f), FontStyle.Bold, GraphicsUnit.Pixel))
                        using (Font uf = new Font("Consolas", Math.Max(5f, lcd.Height * 0.26f), FontStyle.Bold, GraphicsUnit.Pixel))
                        using (SolidBrush gb = new SolidBrush(Color.FromArgb(61, 255, 122)))
                        {
                            g.TextRenderingHint = TextRenderingHint.AntiAlias;
                            StringFormat cf = new StringFormat(); cf.Alignment = StringAlignment.Center; cf.LineAlignment = StringAlignment.Center;
                            g.DrawString("2850", df, gb, new RectangleF(lcd.X, lcd.Y - lcd.Height * 0.06f, lcd.Width, lcd.Height * 0.66f), cf);
                            g.DrawString("MHz +", uf, gb, new RectangleF(lcd.X, lcd.Y + lcd.Height * 0.58f, lcd.Width, lcd.Height * 0.4f), cf);
                        }
                        g.FillPolygon(Brushes.OrangeRed, new PointF[] { new PointF(lcd.Right - w * 0.04f, lcd.Y + 1), new PointF(lcd.Right - w * 0.16f, lcd.Y + 1), new PointF(lcd.Right - w * 0.10f, lcd.Y - h * 0.1f) });
                        break;
                    }
                case "gpu":
                    {
                        float bx = ic.X + w * 0.13f, by = ic.Y + h * 0.06f, bw = w * 0.87f, bh = h * 0.66f;
                        RectangleF card = new RectangleF(bx, by, bw, bh);
                        using (GraphicsPath cp = Xp.Round(Rectangle.Round(card), 3))
                        {
                            using (LinearGradientBrush sh = new LinearGradientBrush(card, Color.FromArgb(82, 90, 108), Color.FromArgb(24, 28, 38), 90f)) g.FillPath(sh, cp);
                            using (Pen sp = new Pen(Color.FromArgb(190, 200, 215), 1f)) g.DrawPath(sp, cp);
                        }
                        using (Pen led = new Pen(Color.FromArgb(0, 214, 255), 1.6f)) g.DrawLine(led, bx + bw * 0.08f, by + 2.6f, bx + bw * 0.92f, by + 2.6f);
                        float fr2 = bh * 0.33f;
                        for (int k = 0; k < 2; k++)
                        {
                            float fx = bx + bw * (k == 0 ? 0.30f : 0.70f), fy = by + bh * 0.55f;
                            using (SolidBrush dark = new SolidBrush(Color.FromArgb(10, 14, 22))) g.FillEllipse(dark, fx - fr2, fy - fr2, fr2 * 2, fr2 * 2);
                            using (Pen rg = new Pen(Color.FromArgb(170, 185, 205), 1f)) g.DrawEllipse(rg, fx - fr2, fy - fr2, fr2 * 2, fr2 * 2);
                            using (SolidBrush bl = new SolidBrush(Color.FromArgb(215, 225, 240)))
                                for (int i = 0; i < 5; i++)
                                {
                                    GraphicsState st = g.Save();
                                    g.TranslateTransform(fx, fy);
                                    g.RotateTransform(i * 72 + k * 20);
                                    g.FillEllipse(bl, -fr2 * 0.16f, -fr2 * 0.92f, fr2 * 0.32f, fr2 * 0.72f);
                                    g.Restore(st);
                                }
                            g.FillEllipse(Brushes.Gold, fx - 1.5f, fy - 1.5f, 3, 3);
                        }
                        using (LinearGradientBrush br2 = new LinearGradientBrush(new RectangleF(ic.X, by - 1, w * 0.1f, bh + 2), Color.Gainsboro, Xp.Dim, 0f))
                            g.FillRectangle(br2, ic.X, by - 1, w * 0.1f, bh + 2);
                        g.FillRectangle(Brushes.Black, ic.X + w * 0.025f, by + bh * 0.2f, w * 0.05f, bh * 0.16f);
                        g.FillRectangle(Brushes.Black, ic.X + w * 0.025f, by + bh * 0.5f, w * 0.05f, bh * 0.16f);
                        using (SolidBrush gold = new SolidBrush(Color.FromArgb(235, 190, 60)))
                            for (int k = 0; k < 9; k++)
                                if (k != 5) g.FillRectangle(gold, bx + bw * 0.08f + k * (bw * 0.075f), by + bh, bw * 0.05f, h * 0.1f);
                        break;
                    }
                case "chip":
                    {
                        RectangleF body = new RectangleF(ic.X + w * 0.18f, ic.Y + h * 0.18f, w * 0.64f, h * 0.64f);
                        using (Pen pin = new Pen(Color.FromArgb(90, 90, 90), 1.5f))
                            for (int i = 0; i < 4; i++)
                            {
                                float fx = body.X + body.Width * (i + 0.5f) / 4f, fy = body.Y + body.Height * (i + 0.5f) / 4f;
                                g.DrawLine(pin, fx, ic.Y, fx, body.Y); g.DrawLine(pin, fx, body.Bottom, fx, ic.Bottom);
                                g.DrawLine(pin, ic.X, fy, body.X, fy); g.DrawLine(pin, body.Right, fy, ic.Right, fy);
                            }
                        using (LinearGradientBrush bb = new LinearGradientBrush(body, Color.FromArgb(90, 105, 135), Color.FromArgb(35, 45, 70), 90f)) g.FillRectangle(bb, body);
                        g.DrawRectangle(Pens.Silver, body.X, body.Y, body.Width, body.Height);
                        g.FillRectangle(Xp.HiBr, cx - w * 0.12f, cy - h * 0.12f, w * 0.24f, h * 0.24f);
                        break;
                    }
                case "chart":
                    {
                        RectangleF scr = new RectangleF(ic.X, ic.Y + h * 0.04f, w, h * 0.7f);
                        g.FillRectangle(Brushes.Black, scr);
                        g.DrawRectangle(Pens.Gray, scr.X, scr.Y, scr.Width, scr.Height);
                        PointF[] pts = { new PointF(scr.X + 2, scr.Bottom - 4), new PointF(scr.X + w * 0.25f, scr.Y + scr.Height * 0.55f), new PointF(scr.X + w * 0.45f, scr.Bottom - 6),
                                         new PointF(scr.X + w * 0.65f, scr.Y + scr.Height * 0.2f), new PointF(scr.X + w * 0.82f, scr.Y + scr.Height * 0.6f), new PointF(scr.Right - 2, scr.Y + 4) };
                        using (Pen ln = new Pen(Color.Lime, 1.6f)) g.DrawLines(ln, pts);
                        g.FillRectangle(Brushes.Gray, cx - w * 0.15f, scr.Bottom + 1, w * 0.3f, h * 0.1f);
                        g.FillRectangle(Brushes.Gray, cx - w * 0.28f, scr.Bottom + 1 + h * 0.1f, w * 0.56f, h * 0.06f);
                        break;
                    }
                case "thermo":
                    {
                        RectangleF tube = new RectangleF(cx - w * 0.11f, ic.Y, w * 0.22f, h * 0.72f);
                        g.FillRectangle(Xp.HiBr, tube);
                        g.DrawRectangle(Pens.DimGray, tube.X, tube.Y, tube.Width, tube.Height);
                        using (SolidBrush red = new SolidBrush(Color.FromArgb(220, 30, 30)))
                        {
                            g.FillRectangle(red, cx - w * 0.05f, ic.Y + h * 0.3f, w * 0.1f, h * 0.45f);
                            g.FillEllipse(red, cx - w * 0.22f, ic.Y + h * 0.62f, w * 0.44f, w * 0.44f);
                        }
                        g.DrawEllipse(Pens.DimGray, cx - w * 0.22f, ic.Y + h * 0.62f, w * 0.44f, w * 0.44f);
                        for (int i = 0; i < 4; i++) g.DrawLine(Pens.DimGray, tube.Right + 2, ic.Y + h * (0.1f + i * 0.14f), tube.Right + 2 + w * 0.15f, ic.Y + h * (0.1f + i * 0.14f));
                        break;
                    }
                case "bench":
                    {
                        // fogo: chama externa (vermelho/laranja) e chama interna (amarelo)
                        PointF[] fo = FlamePts(ic, 0f);
                        using (GraphicsPath fp = new GraphicsPath())
                        {
                            fp.AddBezier(fo[0], fo[1], fo[2], fo[3]); fp.AddBezier(fo[3], fo[4], fo[5], fo[6]); fp.AddBezier(fo[6], fo[7], fo[8], fo[9]);
                            fp.AddBezier(fo[9], fo[10], fo[11], fo[12]); fp.AddBezier(fo[12], fo[13], fo[14], fo[0]); fp.CloseFigure();
                            using (LinearGradientBrush fg = new LinearGradientBrush(new RectangleF(ic.X, ic.Y, w, h), Color.FromArgb(214, 40, 20), Color.FromArgb(255, 160, 0), 90f)) g.FillPath(fg, fp);
                            using (Pen fpn = new Pen(Color.FromArgb(150, 40, 10), 1f)) g.DrawPath(fpn, fp);
                        }
                        PointF[] fi = FlamePts(ic, 1f);
                        using (GraphicsPath fp2 = new GraphicsPath())
                        {
                            fp2.AddBezier(fi[0], fi[1], fi[2], fi[3]); fp2.AddBezier(fi[3], fi[4], fi[5], fi[6]); fp2.AddBezier(fi[6], fi[7], fi[8], fi[9]);
                            fp2.AddBezier(fi[9], fi[10], fi[11], fi[12]); fp2.AddBezier(fi[12], fi[13], fi[14], fi[0]); fp2.CloseFigure();
                            using (LinearGradientBrush fg2 = new LinearGradientBrush(new RectangleF(ic.X, ic.Y + h * 0.35f, w, h * 0.65f), Color.FromArgb(255, 214, 40), Color.FromArgb(255, 248, 190), 90f)) g.FillPath(fg2, fp2);
                        }
                        break;
                    }
                case "cpuoc":
                    {
                        RectangleF body = new RectangleF(ic.X + w * 0.16f, ic.Y + h * 0.16f, w * 0.68f, h * 0.68f);
                        using (Pen pin = new Pen(Color.FromArgb(90, 90, 90), 1.5f))
                            for (int i = 0; i < 4; i++)
                            {
                                float fx = body.X + body.Width * (i + 0.5f) / 4f, fy = body.Y + body.Height * (i + 0.5f) / 4f;
                                g.DrawLine(pin, fx, ic.Y, fx, body.Y); g.DrawLine(pin, fx, body.Bottom, fx, ic.Bottom);
                                g.DrawLine(pin, ic.X, fy, body.X, fy); g.DrawLine(pin, body.Right, fy, ic.Right, fy);
                            }
                        using (LinearGradientBrush bb = new LinearGradientBrush(body, Color.FromArgb(84, 100, 132), Color.FromArgb(30, 40, 66), 90f)) g.FillRectangle(bb, body);
                        g.DrawRectangle(Pens.Silver, body.X, body.Y, body.Width, body.Height);
                        PointF[] bolt = {
                            new PointF(cx + w * 0.12f, ic.Y + h * 0.1f), new PointF(cx - w * 0.16f, cy + h * 0.04f), new PointF(cx - w * 0.01f, cy + h * 0.04f),
                            new PointF(cx - w * 0.12f, ic.Y + h * 0.92f), new PointF(cx + w * 0.18f, cy - h * 0.06f), new PointF(cx + w * 0.03f, cy - h * 0.06f) };
                        using (LinearGradientBrush fb = new LinearGradientBrush(new RectangleF(ic.X, ic.Y, w, h), Color.FromArgb(255, 235, 90), Color.FromArgb(255, 120, 10), 90f)) g.FillPolygon(fb, bolt);
                        using (Pen op = new Pen(Color.FromArgb(120, 50, 0), 1f)) g.DrawPolygon(op, bolt);
                        break;
                    }
                case "ram":
                    {
                        RectangleF pcb = new RectangleF(ic.X, ic.Y + h * 0.2f, w, h * 0.48f);
                        using (LinearGradientBrush pg = new LinearGradientBrush(pcb, Color.FromArgb(40, 160, 84), Color.FromArgb(8, 84, 40), 90f)) g.FillRectangle(pg, pcb);
                        g.DrawRectangle(Pens.DarkGreen, pcb.X, pcb.Y, pcb.Width, pcb.Height);
                        for (int i = 0; i < 4; i++)
                        {
                            RectangleF chip = new RectangleF(ic.X + w * 0.07f + i * (w * 0.225f), pcb.Y + pcb.Height * 0.16f, w * 0.17f, pcb.Height * 0.68f);
                            g.FillRectangle(Brushes.Black, chip);
                            g.FillRectangle(Brushes.DimGray, chip.X + 1, chip.Y + 1, chip.Width - 2, 2);
                        }
                        float pw2 = (w - 4) / 12f;
                        using (SolidBrush gold = new SolidBrush(Color.FromArgb(235, 190, 60)))
                            for (int i = 0; i < 12; i++)
                                if (i != 5) g.FillRectangle(gold, ic.X + 2 + i * pw2, pcb.Bottom, pw2 * 0.62f, h * 0.13f);
                        g.FillRectangle(Brushes.Silver, ic.X - 2, pcb.Y - 3, 3, pcb.Height + 6);
                        g.FillRectangle(Brushes.Silver, ic.Right - 1, pcb.Y - 3, 3, pcb.Height + 6);
                        break;
                    }
                case "net":
                    {
                        RectangleF gb = new RectangleF(ic.X + 1, ic.Y + 1, w - 2, h - 2);
                        using (LinearGradientBrush gbr = new LinearGradientBrush(gb, Color.FromArgb(110, 190, 255), Color.FromArgb(20, 90, 200), 90f)) g.FillEllipse(gbr, gb);
                        using (Pen gp = new Pen(Color.FromArgb(220, 255, 255, 255), 1.3f))
                        {
                            g.DrawEllipse(gp, gb.X + gb.Width * 0.28f, gb.Y, gb.Width * 0.44f, gb.Height);
                            g.DrawLine(gp, gb.X, cy, gb.Right, cy);
                            g.DrawArc(gp, gb.X + gb.Width * 0.06f, gb.Y + gb.Height * 0.2f, gb.Width * 0.88f, gb.Height * 0.6f, 0, 180);
                            g.DrawArc(gp, gb.X + gb.Width * 0.06f, gb.Y + gb.Height * 0.2f, gb.Width * 0.88f, gb.Height * 0.6f, 180, 180);
                        }
                        g.DrawEllipse(Pens.Navy, gb);
                        break;
                    }
                case "health":
                    {
                        // HDD de cima com faixa "SMART" + check verde (saude ok)
                        Func<float, float> ux = delegate(float u) { return ic.X + u / 100f * w; };
                        Func<float, float> uy = delegate(float v) { return ic.Y + v / 100f * h; };
                        RectangleF body = new RectangleF(ux(6), uy(10), w * 0.88f, h * 0.8f);
                        using (GraphicsPath bp2 = Xp.Round(Rectangle.Round(body), 3))
                        {
                            using (LinearGradientBrush bg = new LinearGradientBrush(body, Color.FromArgb(207, 214, 226), Color.FromArgb(93, 103, 121), 90f)) g.FillPath(bg, bp2);
                            using (Pen bo = new Pen(Color.FromArgb(43, 51, 71), 1.4f)) g.DrawPath(bo, bp2);
                        }
                        float pcx = ux(44), pcy = uy(44), pr = w * 0.30f;
                        using (LinearGradientBrush pl = new LinearGradientBrush(new RectangleF(pcx - pr, pcy - pr, pr * 2, pr * 2), Color.FromArgb(246, 248, 252), Color.FromArgb(150, 160, 178), 45f)) g.FillEllipse(pl, pcx - pr, pcy - pr, pr * 2, pr * 2);
                        using (Pen pro = new Pen(Color.FromArgb(74, 84, 104), 1f)) { g.DrawEllipse(pro, pcx - pr, pcy - pr, pr * 2, pr * 2); g.DrawEllipse(pro, pcx - pr * 0.66f, pcy - pr * 0.66f, pr * 1.32f, pr * 1.32f); }
                        g.FillEllipse(Brushes.DimGray, pcx - pr * 0.16f, pcy - pr * 0.16f, pr * 0.32f, pr * 0.32f);
                        float ax = ux(86), ay = uy(70);
                        using (Pen arm = new Pen(Color.FromArgb(43, 51, 71), Math.Max(2f, w * 0.05f))) { arm.StartCap = LineCap.Round; arm.EndCap = LineCap.Round; g.DrawLine(arm, ax, ay, pcx + pr * 0.5f, pcy + pr * 0.25f); }
                        g.FillEllipse(Brushes.Goldenrod, ax - 2.2f, ay - 2.2f, 4.4f, 4.4f);
                        RectangleF band = new RectangleF(ux(10), uy(62), w * 0.80f, h * 0.26f);
                        using (GraphicsPath bnd = Xp.Round(Rectangle.Round(band), 2))
                        {
                            using (SolidBrush bb = new SolidBrush(Color.FromArgb(15, 26, 46))) g.FillPath(bb, bnd);
                            using (Pen bw = new Pen(Color.White, 1f)) g.DrawPath(bw, bnd);
                        }
                        using (Font sf = new Font("Arial", Math.Max(6f, band.Height * 0.52f), FontStyle.Bold, GraphicsUnit.Pixel))
                        using (SolidBrush gr = new SolidBrush(Color.FromArgb(61, 255, 122)))
                        {
                            g.TextRenderingHint = TextRenderingHint.AntiAlias;
                            StringFormat sfm = new StringFormat(); sfm.Alignment = StringAlignment.Center; sfm.LineAlignment = StringAlignment.Center;
                            g.DrawString("SMART", sf, gr, new RectangleF(band.X, band.Y, band.Width * 0.66f, band.Height), sfm);
                            sfm.Dispose();
                        }
                        using (Pen ck = new Pen(Color.FromArgb(61, 255, 122), Math.Max(1.6f, w * 0.045f))) { ck.StartCap = LineCap.Round; ck.EndCap = LineCap.Round; ck.LineJoin = LineJoin.Round; g.DrawLines(ck, new PointF[] { new PointF(band.X + band.Width * 0.72f, band.Y + band.Height * 0.55f), new PointF(band.X + band.Width * 0.82f, band.Y + band.Height * 0.76f), new PointF(band.X + band.Width * 0.96f, band.Y + band.Height * 0.24f) }); }
                        break;
                    }
                case "opt":
                    {
                        // simbolo do Windows XP: bandeira ondulada com quatro paineis (vermelho, verde, azul, amarelo)
                        Color[] fc = { Color.FromArgb(232, 66, 36), Color.FromArgb(118, 184, 0), Color.FromArgb(36, 122, 214), Color.FromArgb(255, 188, 0) };
                        for (int pi = 0; pi < 4; pi++)
                        {
                            float u0 = (pi % 2 == 0) ? 0f : 0.53f, u1 = (pi % 2 == 0) ? 0.47f : 1f, v0 = (pi < 2) ? 0f : 0.53f, v1 = (pi < 2) ? 0.47f : 1f;
                            List<PointF> poly = new List<PointF>();
                            int ns = 8;
                            for (int k = 0; k <= ns; k++) poly.Add(FlagPt(ic, u0 + (u1 - u0) * k / ns, v0));
                            for (int k = 0; k <= ns; k++) poly.Add(FlagPt(ic, u1, v0 + (v1 - v0) * k / ns));
                            for (int k = ns; k >= 0; k--) poly.Add(FlagPt(ic, u0 + (u1 - u0) * k / ns, v1));
                            for (int k = ns; k >= 0; k--) poly.Add(FlagPt(ic, u0, v0 + (v1 - v0) * k / ns));
                            PointF[] pa = poly.ToArray();
                            RectangleF pb = new RectangleF(ic.X, ic.Y, w, h);
                            using (LinearGradientBrush fb2 = new LinearGradientBrush(pb, ControlPaint.Light(fc[pi], 0.35f), ControlPaint.Dark(fc[pi], 0.12f), 70f)) g.FillPolygon(fb2, pa);
                            using (Pen fo = new Pen(Color.FromArgb(90, 20, 20, 20), 0.8f)) g.DrawPolygon(fo, pa);
                        }
                        break;
                    }
                case "ssd":
                    {
                        RectangleF body = new RectangleF(ic.X, ic.Y + h * 0.14f, w, h * 0.72f);
                        using (LinearGradientBrush bg = new LinearGradientBrush(body, Color.FromArgb(120, 128, 142), Color.FromArgb(52, 58, 70), 90f)) g.FillRectangle(bg, body);
                        g.DrawRectangle(Pens.Black, body.X, body.Y, body.Width, body.Height);
                        RectangleF lab = new RectangleF(body.X + w * 0.1f, body.Y + body.Height * 0.16f, w * 0.52f, body.Height * 0.5f);
                        using (SolidBrush lb = new SolidBrush(Color.FromArgb(70, 140, 220))) g.FillRectangle(lb, lab);
                        g.DrawRectangle(Pens.White, lab.X, lab.Y, lab.Width, lab.Height);
                        using (SolidBrush led = new SolidBrush(Color.FromArgb(60, 230, 90))) g.FillEllipse(led, body.Right - w * 0.2f, body.Bottom - body.Height * 0.3f, w * 0.1f, w * 0.1f);
                        using (SolidBrush gold = new SolidBrush(Color.FromArgb(235, 190, 60)))
                            for (int i = 0; i < 6; i++) g.FillRectangle(gold, body.X + w * 0.08f + i * w * 0.07f, body.Bottom, w * 0.045f, h * 0.1f);
                        break;
                    }
                case "fpu":
                    {
                        using (Font ff = new Font("Arial", h * 0.62f, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel))
                        {
                            g.TextRenderingHint = TextRenderingHint.AntiAlias;
                            using (SolidBrush sb1 = new SolidBrush(Color.FromArgb(40, 90, 190))) g.DrawString("\u03C0", ff, sb1, ic.X - w * 0.02f, ic.Y - h * 0.08f);
                        }
                        using (Pen wp = new Pen(Color.FromArgb(230, 120, 30), 2f))
                        {
                            PointF[] wv = new PointF[16];
                            for (int i = 0; i < 16; i++) wv[i] = new PointF(ic.X + i * w / 15f, ic.Bottom - h * 0.18f - (float)Math.Sin(i * 0.9) * h * 0.14f);
                            g.DrawCurve(wp, wv);
                        }
                        g.DrawRectangle(Pens.SlateGray, ic.X, ic.Y, w, h);
                        break;
                    }
                case "trash":
                    {
                        RectangleF can = new RectangleF(ic.X + w * 0.16f, ic.Y + h * 0.26f, w * 0.68f, h * 0.7f);
                        using (LinearGradientBrush cb = new LinearGradientBrush(can, Color.FromArgb(214, 224, 238), Color.FromArgb(130, 146, 170), 0f)) g.FillRectangle(cb, can);
                        g.DrawRectangle(Pens.DimGray, can.X, can.Y, can.Width, can.Height);
                        using (Pen sl = new Pen(Color.FromArgb(110, 124, 146), 1.4f))
                            for (int i = 1; i < 4; i++) g.DrawLine(sl, can.X + can.Width * i / 4f, can.Y + can.Height * 0.14f, can.X + can.Width * i / 4f, can.Bottom - can.Height * 0.12f);
                        RectangleF lid = new RectangleF(ic.X + w * 0.08f, ic.Y + h * 0.16f, w * 0.84f, h * 0.1f);
                        g.FillRectangle(Brushes.DimGray, lid);
                        g.FillRectangle(Brushes.DimGray, ic.X + w * 0.38f, ic.Y + h * 0.06f, w * 0.24f, h * 0.1f);
                        using (Pen xp = new Pen(Color.FromArgb(210, 40, 40), 2.4f))
                        {
                            g.DrawLine(xp, ic.X + w * 0.66f, ic.Y + h * 0.5f, ic.X + w * 0.94f, ic.Y + h * 0.78f);
                            g.DrawLine(xp, ic.X + w * 0.94f, ic.Y + h * 0.5f, ic.X + w * 0.66f, ic.Y + h * 0.78f);
                        }
                        break;
                    }
                case "aff":
                    {
                        float cs = Math.Min(w, h) / 3.3f, gap = cs * 0.2f;
                        bool[] sel = { true, true, false, false, true, true, true, false, true };
                        for (int r = 0; r < 3; r++)
                            for (int c = 0; c < 3; c++)
                            {
                                RectangleF cell = new RectangleF(ic.X + c * (cs + gap), ic.Y + r * (cs + gap), cs, cs);
                                if (sel[r * 3 + c])
                                {
                                    using (LinearGradientBrush cb = new LinearGradientBrush(cell, Color.FromArgb(120, 180, 255), Color.FromArgb(20, 90, 210), 90f)) g.FillRectangle(cb, cell);
                                    g.DrawRectangle(Pens.Navy, cell.X, cell.Y, cell.Width, cell.Height);
                                    using (Pen ck = new Pen(Xp.Hi, 1.6f))
                                        g.DrawLines(ck, new PointF[] { new PointF(cell.X + cs * 0.22f, cell.Y + cs * 0.52f), new PointF(cell.X + cs * 0.42f, cell.Y + cs * 0.74f), new PointF(cell.X + cs * 0.8f, cell.Y + cs * 0.26f) });
                                }
                                else
                                {
                                    g.FillRectangle(Xp.HiBr, cell);
                                    g.DrawRectangle(Pens.Gray, cell.X, cell.Y, cell.Width, cell.Height);
                                }
                            }
                        break;
                    }
                case "paypal":
                    {
                        using (Font f = new Font("Arial", h * 1.05f, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel))
                        {
                            g.TextRenderingHint = TextRenderingHint.AntiAlias;
                            using (SolidBrush b2 = new SolidBrush(Color.FromArgb(0, 156, 222))) g.DrawString("P", f, b2, ic.X + w * 0.12f, ic.Y - h * 0.14f);
                            using (SolidBrush b1 = new SolidBrush(Color.FromArgb(0, 48, 135))) g.DrawString("P", f, b1, ic.X - w * 0.02f, ic.Y - h * 0.22f);
                        }
                        break;
                    }
                case "livepix":
                    {
                        using (GraphicsPath hp = new GraphicsPath())
                        {
                            hp.AddBezier(cx, ic.Y + h, ic.X - w * 0.15f, ic.Y + h * 0.45f, ic.X + w * 0.1f, ic.Y - h * 0.1f, cx, ic.Y + h * 0.28f);
                            hp.AddBezier(cx, ic.Y + h * 0.28f, ic.X + w * 0.9f, ic.Y - h * 0.1f, ic.X + w * 1.15f, ic.Y + h * 0.45f, cx, ic.Y + h);
                            hp.CloseFigure();
                            using (LinearGradientBrush hb = new LinearGradientBrush(ic, Color.FromArgb(150, 110, 255), Color.FromArgb(90, 50, 200), 90f)) g.FillPath(hb, hp);
                            using (Pen hpn = new Pen(Color.FromArgb(60, 30, 140))) g.DrawPath(hpn, hp);
                        }
                        PointF[] tri = { new PointF(cx - w * 0.1f, cy - h * 0.12f), new PointF(cx - w * 0.1f, cy + h * 0.2f), new PointF(cx + w * 0.16f, cy + h * 0.04f) };
                        g.FillPolygon(Xp.HiBr, tri);
                        break;
                    }
            }
        }
    }

    // Barra de progresso (blocos verdes no XP, vidro verde no Aero)
    class XpProgress : Control
    {
        int val, tick;
        public bool Marquee;
        public XpProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 18;
        }
        public int Value { get { return val; } set { int v = Math.Max(0, Math.Min(100, value)); if (v != val) { val = v; Invalidate(); } } }
        public void Step() { tick++; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            if (Xp.Aero)
            {
                using (GraphicsPath p = Xp.Round(r, 3))
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(228, 232, 238))) g.FillPath(bg, p);
                    g.SetClip(p);
                    int fw = Marquee ? Width / 4 : (Width - 2) * val / 100;
                    int fx = Marquee ? (tick * 14) % (Width + Width / 4) - Width / 4 : 1;
                    if (fw > 0)
                    {
                        Rectangle fr = new Rectangle(fx, 1, fw, Height - 2);
                        using (LinearGradientBrush gb = new LinearGradientBrush(fr, Color.FromArgb(170, 240, 130), Color.FromArgb(40, 170, 30), 90f)) g.FillRectangle(gb, fr);
                        using (SolidBrush sh = new SolidBrush(Color.FromArgb(90, 255, 255, 255))) g.FillRectangle(sh, fr.X, fr.Y, fr.Width, fr.Height / 2);
                    }
                    g.ResetClip();
                    using (Pen bp = new Pen(Color.FromArgb(120, 130, 140))) g.DrawPath(bp, p);
                }
            }
            else
            {
                using (SolidBrush pb = new SolidBrush(Xp.ProgBg)) g.FillRectangle(pb, r);
                using (Pen bp = new Pen(Xp.ProgBorder)) g.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
                int bw = 8, gap = 2, blocks = Math.Max(1, (Width - 6) / (bw + gap));
                int shown = Marquee ? 4 : blocks * val / 100;
                for (int i = 0; i < shown; i++)
                {
                    int bi = Marquee ? (tick + i) % blocks : i;
                    Rectangle b = new Rectangle(3 + bi * (bw + gap), 3, bw, Height - 6);
                    using (LinearGradientBrush gb = new LinearGradientBrush(b, Xp.Prog1, Xp.Prog2, 90f)) g.FillRectangle(gb, b);
                }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Janela base com barra de titulo Luna (azul) desenhada a mao
    // ------------------------------------------------------------------
    class XpForm : Form
    {
        protected const int TB = 30;
        protected virtual void OnMinimizeClick() { WindowState = FormWindowState.Minimized; }
        Bitmap logo = Xp.Logo(1);
        bool hotClose, hotMin;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        public XpForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Xp.Face; Font = Xp.Ui; DoubleBuffered = true;
            ApplyPadding();
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.FromHandle(Xp.Logo(2).GetHicon());
            Xp.Forms.Add(this);
            FormClosed += delegate { Xp.Forms.Remove(this); };
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.Style |= 0x20000; return cp; } // WS_MINIMIZEBOX
        }
        Rectangle CloseR { get { return new Rectangle(Width - 27, 5, 21, 21); } }
        Rectangle MinR { get { return new Rectangle(Width - 51, 5, 21, 21); } }

        void ApplyPadding() { Padding = new Padding(4, TB, 4, 4); }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); Xp.Apply(this); Lang.Apply(this, null); }
        public void RefreshFrame() { ApplyPadding(); UpdateRegion(); Invalidate(); }
        void UpdateRegion()
        {
            if (Width < 20 || Height < 20) return;
            GraphicsPath p = new GraphicsPath();
            if (Xp.Aero)
            {
                p.AddArc(0, 0, 16, 16, 180, 90);
                p.AddArc(Width - 16, 0, 16, 16, 270, 90);
                p.AddArc(Width - 16, Height - 16, 16, 16, 0, 90);
                p.AddArc(0, Height - 16, 16, 16, 90, 90);
                p.CloseFigure();
            }
            else if (Xp.Square) p.AddRectangle(new Rectangle(0, 0, Width, Height));
            else
            {
                p.AddArc(0, 0, 16, 16, 180, 90);
                p.AddArc(Width - 16, 0, 16, 16, 270, 90);
                p.AddLine(Width, 8, Width, Height);
                p.AddLine(Width, Height, 0, Height);
                p.CloseFigure();
            }
            Region = new Region(p);
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateRegion(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool c = CloseR.Contains(e.Location), m = MinR.Contains(e.Location);
            if (c != hotClose || m != hotMin) { hotClose = c; hotMin = m; Invalidate(new Rectangle(0, 0, Width, TB)); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < TB)
            {
                if (CloseR.Contains(e.Location)) { Close(); return; }
                if (MinR.Contains(e.Location)) { OnMinimizeClick(); return; }
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 2, 0); // WM_NCLBUTTONDOWN, HTCAPTION
            }
            base.OnMouseDown(e);
        }

        void CapLuna(Graphics g, Rectangle r, bool close, bool hot)
        {
            Color c1 = close ? Xp.CapClose1 : Xp.CapBtn1;
            Color c2 = close ? Xp.CapClose2 : Xp.CapBtn2;
            if (hot && !Xp.Bevel) { c1 = ControlPaint.Light(c1); c2 = ControlPaint.Light(c2); }
            if (Xp.Bevel) Xp.Bevel3D(g, r, false, c1);
            else
            using (GraphicsPath p = Xp.Round(r, 3))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(r, c1, c2, 90f)) g.FillPath(br, p);
                using (Pen pen = new Pen(Xp.Theme == 0 ? Color.White : Xp.CapText, 1.5f)) g.DrawPath(pen, p);
            }
            using (Pen x = new Pen(Xp.CapX, 2f))
            {
                if (close)
                {
                    g.DrawLine(x, r.X + 6, r.Y + 6, r.Right - 7, r.Bottom - 7);
                    g.DrawLine(x, r.Right - 7, r.Y + 6, r.X + 6, r.Bottom - 7);
                }
                else g.DrawLine(x, r.X + 6, r.Bottom - 7, r.X + 12, r.Bottom - 7);
            }
        }

        void CapAero(Graphics g, Rectangle r, bool close, bool hot)
        {
            Color t1, t2, b1, b2;
            if (close)
            {
                t1 = hot ? Color.FromArgb(247, 160, 140) : Color.FromArgb(228, 160, 150);
                t2 = hot ? Color.FromArgb(234, 106, 86) : Color.FromArgb(208, 108, 98);
                b1 = hot ? Color.FromArgb(222, 54, 34) : Color.FromArgb(192, 64, 54);
                b2 = hot ? Color.FromArgb(242, 96, 66) : Color.FromArgb(214, 96, 86);
            }
            else
            {
                t1 = hot ? Color.FromArgb(234, 247, 255) : Color.FromArgb(216, 232, 249);
                t2 = hot ? Color.FromArgb(192, 228, 251) : Color.FromArgb(182, 210, 241);
                b1 = hot ? Color.FromArgb(122, 192, 241) : Color.FromArgb(122, 162, 216);
                b2 = hot ? Color.FromArgb(162, 217, 251) : Color.FromArgb(152, 187, 231);
            }
            Xp.Glass(g, new Rectangle(r.X, r.Y - 6, r.Width, r.Height + 6), 5, t1, t2, b1, b2, Color.FromArgb(50, 70, 100));
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            using (Pen dk = new Pen(Color.FromArgb(130, 10, 20, 50), 3.4f))
            using (Pen wh = new Pen(Xp.Hi, 2f))
            {
                if (close)
                {
                    g.DrawLine(dk, cx - 4, cy - 3, cx + 4, cy + 4); g.DrawLine(dk, cx + 4, cy - 3, cx - 4, cy + 4);
                    g.DrawLine(wh, cx - 4, cy - 3, cx + 4, cy + 4); g.DrawLine(wh, cx + 4, cy - 3, cx - 4, cy + 4);
                }
                else
                {
                    g.DrawLine(dk, cx - 4, cy + 3, cx + 4, cy + 3);
                    g.DrawLine(wh, cx - 4, cy + 3, cx + 4, cy + 3);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Xp.Aero) PaintAero(g); else PaintLuna(g);
        }

        void PaintLuna(Graphics g)
        {
            using (SolidBrush fr = new SolidBrush(Xp.CapFrame)) g.FillRectangle(fr, ClientRectangle);
            Rectangle tb = new Rectangle(0, 0, Width, TB);
            if (Xp.Theme == 0)
            {
                using (LinearGradientBrush br = new LinearGradientBrush(tb, Color.FromArgb(9, 151, 255), Color.FromArgb(0, 80, 238), 90f))
                {
                    ColorBlend cb = new ColorBlend(5);
                    cb.Positions = new float[] { 0f, 0.1f, 0.5f, 0.9f, 1f };
                    cb.Colors = new Color[] { Color.FromArgb(9, 151, 255), Color.FromArgb(0, 83, 238), Color.FromArgb(0, 80, 238), Color.FromArgb(0, 96, 250), Color.FromArgb(0, 60, 210) };
                    br.InterpolationColors = cb;
                    g.FillRectangle(br, tb);
                }
            }
            else if (Xp.Bevel)
            {
                Rectangle tb2 = new Rectangle(4, 4, Width - 8, TB - 6);
                using (LinearGradientBrush br = new LinearGradientBrush(tb2, Xp.CapA, Xp.CapB, 0f)) g.FillRectangle(br, tb2);
            }
            else
            {
                using (LinearGradientBrush br = new LinearGradientBrush(tb, Xp.CapA, Xp.CapB, 90f)) g.FillRectangle(br, tb);
                using (Pen hl = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawLine(hl, 8, 1, Width - 8, 1);
            }
            using (SolidBrush fb = new SolidBrush(Xp.Face)) g.FillRectangle(fb, 4, TB, Width - 8, Height - TB - 4);
            g.DrawImageUnscaled(logo, 6, 5);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            if (!Xp.Bevel) using (SolidBrush sh = new SolidBrush(Xp.CapShadow)) g.DrawString(Text, Xp.Caption, sh, 31, 8);
            using (SolidBrush tw = new SolidBrush(Xp.CapText)) g.DrawString(Text, Xp.Caption, tw, 30, 7);
            CapLuna(g, MinR, false, hotMin);
            CapLuna(g, CloseR, true, hotClose);
            if (Xp.Bevel) Xp.Bevel3D(g, new Rectangle(0, 0, Width, Height), false, null);
            else if (Xp.Theme == 3) using (Pen np = new Pen(Color.FromArgb(200, 255, 60, 220), 1.5f)) g.DrawRectangle(np, 2, TB - 1, Width - 5, Height - TB - 2);
        }

        static void Blob(Graphics g, Rectangle r, Color c)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = c;
                    b.SurroundColors = new Color[] { Color.FromArgb(0, c.R, c.G, c.B) };
                    g.FillPath(b, p);
                }
            }
        }

        void PaintAero(Graphics g)
        {
            Rectangle all = ClientRectangle;
            // vidro: degrade azul-claro + "papel de parede" desfocado por tras
            using (LinearGradientBrush br = new LinearGradientBrush(all, Color.FromArgb(210, 229, 248), Color.FromArgb(160, 192, 232), 90f))
                g.FillRectangle(br, all);
            Blob(g, new Rectangle(-40, -50, 340, 150), Color.FromArgb(150, 90, 205, 235));
            Blob(g, new Rectangle(Width / 2 - 120, -60, 360, 140), Color.FromArgb(110, 120, 190, 255));
            Blob(g, new Rectangle(Width - 330, -40, 340, 140), Color.FromArgb(130, 130, 225, 175));
            Blob(g, new Rectangle(-60, Height - 120, 300, 200), Color.FromArgb(90, 120, 210, 225));
            Blob(g, new Rectangle(Width - 260, Height - 130, 320, 210), Color.FromArgb(90, 150, 225, 160));
            // reflexos diagonais do vidro
            using (GraphicsPath sheen = new GraphicsPath())
            {
                sheen.AddPolygon(new Point[] { new Point(0, 0), new Point(Width * 3 / 5, 0), new Point(Width / 6, Height), new Point(0, Height) });
                using (LinearGradientBrush sb = new LinearGradientBrush(all, Color.FromArgb(105, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0f))
                    g.FillPath(sb, sheen);
            }
            using (GraphicsPath sheen2 = new GraphicsPath())
            {
                sheen2.AddPolygon(new Point[] { new Point(Width * 3 / 4, 0), new Point(Width, 0), new Point(Width, Height / 2), new Point(Width * 2 / 3, Height) });
                using (LinearGradientBrush sb2 = new LinearGradientBrush(all, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(60, 255, 255, 255), 0f))
                    g.FillPath(sb2, sheen2);
            }
            // faixa de brilho na barra de titulo
            using (LinearGradientBrush tg = new LinearGradientBrush(new Rectangle(0, 0, Width, TB), Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                g.FillRectangle(tg, 0, 0, Width, TB);

            Rectangle body = new Rectangle(8, TB, Width - 16, Height - TB - 8);
            using (SolidBrush fb = new SolidBrush(Xp.Face)) g.FillRectangle(fb, body);
            using (Pen ip = new Pen(Color.FromArgb(150, 50, 80, 120))) g.DrawRectangle(ip, body.X - 1, body.Y - 1, body.Width + 1, body.Height + 1);
            using (Pen ip2 = new Pen(Color.FromArgb(190, 255, 255, 255))) g.DrawRectangle(ip2, body.X, body.Y, body.Width - 1, body.Height - 1);
            using (GraphicsPath bp = Xp.Round(new Rectangle(0, 0, Width, Height), 8))
            using (Pen op = new Pen(Color.FromArgb(50, 70, 100))) g.DrawPath(op, bp);
            using (GraphicsPath hp = Xp.Round(new Rectangle(1, 1, Width - 2, Height - 2), 7))
            using (Pen wp = new Pen(Color.FromArgb(170, 255, 255, 255))) g.DrawPath(wp, hp);

            g.DrawImageUnscaled(logo, 8, 5);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(135, 255, 255, 255)))
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                        if ((dx != 0 || dy != 0) && Math.Abs(dx) + Math.Abs(dy) <= 3) g.DrawString(Text, Xp.Caption, glow, 32 + dx, 7 + dy);
            g.DrawString(Text, Xp.Caption, Brushes.Black, 32, 7);
            CapAero(g, MinR, false, hotMin);
            CapAero(g, CloseR, true, hotClose);
        }
    }

    // ------------------------------------------------------------------
    //  Grafico estilo monitor de tarefas (fundo preto, grade verde)
    // ------------------------------------------------------------------
    class Series
    {
        public Sn Sn; public Color Color; public List<float> V = new List<float>();
    }

    class XpGraph : Control
    {
        public const int Store = 3600;               // historico guardado (1 amostra por segundo)
        public List<Series> Items = new List<Series>();
        public bool Normalize = true, Paused;
        public int PeriodMs = 1000;
        int window = 120, hoverX = -1;
        public int Window { get { return window; } set { window = Math.Max(10, value); Invalidate(); } }

        public XpGraph()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = new Font("Tahoma", 7.5f);
            Cursor = Cursors.Cross;
        }
        static bool IsPct(string t) { return t == "Load" || t == "Control" || t == "Level"; }
        static string Secs(double s) { return s >= 120 ? ((int)(s / 60)) + " min" : (s >= 10 ? ((int)s) + " s" : s.ToString("0.0") + " s"); }
        static string Num(float v) { return v.ToString("0.##"); }
        Rectangle Plot() { return new Rectangle(50, 8, Math.Max(10, Width - 60), Math.Max(10, Height - 30)); }

        protected override void OnMouseMove(MouseEventArgs e) { hoverX = e.X; Invalidate(); base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverX = -1; Invalidate(); base.OnMouseLeave(e); }

        void DrawBox(Graphics g, Rectangle area, int x, int y, List<string> rows, List<Color> cols)
        {
            int w = 0;
            foreach (string t in rows) w = Math.Max(w, TextRenderer.MeasureText(t, Font).Width);
            Rectangle b = new Rectangle(x, y, w + 24, rows.Count * 14 + 6);
            if (b.Right > area.Right) b.X = Math.Max(area.Left, area.Right - b.Width);
            if (b.Bottom > area.Bottom) b.Y = Math.Max(area.Top, area.Bottom - b.Height);
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(215, 20, 20, 20))) g.FillRectangle(bg, b);
            g.DrawRectangle(Pens.Gray, b);
            for (int i = 0; i < rows.Count; i++)
            {
                using (SolidBrush sw = new SolidBrush(cols[i])) g.FillRectangle(sw, b.X + 5, b.Y + 6 + i * 14, 8, 8);
                TextRenderer.DrawText(g, rows[i], Font, new Point(b.X + 17, b.Y + 3 + i * 14), cols[i]);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Xp.GraphBg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle pl = Plot();

            float gmax = 0;
            foreach (Series s in Items)
            {
                int n = Math.Min(s.V.Count, window);
                for (int i = s.V.Count - n; i < s.V.Count; i++) gmax = Math.Max(gmax, s.V[i]);
            }
            if (gmax <= 0) gmax = 1;
            gmax *= 1.1f;

            using (Pen gp = new Pen(Xp.GraphGrid))
            {
                for (int i = 0; i <= 10; i++)
                {
                    int y = pl.Top + pl.Height * i / 10;
                    g.DrawLine(gp, pl.Left, y, pl.Right, y);
                    int x = pl.Left + pl.Width * i / 10;
                    g.DrawLine(gp, x, pl.Top, x, pl.Bottom);
                }
            }
            for (int i = 0; i <= 10; i += 2)
            {
                int y = pl.Top + pl.Height * i / 10;
                string lab = Normalize ? (100 - i * 10) + "%" : Num(gmax * (10 - i) / 10f);
                TextRenderer.DrawText(g, lab, Font, new Point(2, y - 6), Xp.GraphText);
            }
            for (int i = 0; i <= 4; i++)
            {
                int x = pl.Left + pl.Width * i / 4;
                string lab = i == 4 ? "agora" : "-" + Secs(window * (4 - i) / 4.0 * PeriodMs / 1000.0);
                TextRenderer.DrawText(g, lab, Font, new Point(x - 14, pl.Bottom + 4), Xp.GraphText);
            }

            float step = pl.Width / (float)(window - 1);
            foreach (Series s in Items)
            {
                int n = Math.Min(s.V.Count, window);
                if (n < 2) continue;
                float scale;
                if (!Normalize) scale = gmax;
                else if (IsPct(s.Sn.Type)) scale = 100f;
                else
                {
                    float m = 0;
                    for (int i = s.V.Count - n; i < s.V.Count; i++) m = Math.Max(m, s.V[i]);
                    scale = m <= 0 ? 1 : m * 1.15f;
                }
                PointF[] pts = new PointF[n];
                for (int i = 0; i < n; i++)
                {
                    float v = s.V[s.V.Count - n + i];
                    float x = pl.Right - (n - 1 - i) * step;
                    float y = pl.Bottom - Math.Max(0f, Math.Min(1f, v / scale)) * pl.Height;
                    pts[i] = new PointF(x, y);
                }
                using (Pen p = new Pen(s.Color, 1.6f)) g.DrawLines(p, pts);
            }

            // legenda com valor atual
            List<string> lt = new List<string>(); List<Color> lc = new List<Color>();
            foreach (Series s in Items)
            {
                if (lt.Count >= 14) break;
                lt.Add(s.Sn.Name + ": " + s.Sn.Fmt(s.Sn.Last)); lc.Add(s.Color);
            }
            if (lt.Count > 0) DrawBox(g, pl, pl.Left + 6, pl.Top + 4, lt, lc);

            // cursor: mostra o valor de cada linha no instante apontado
            if (hoverX >= pl.Left && hoverX <= pl.Right && Items.Count > 0)
            {
                int k = (int)Math.Round((pl.Right - hoverX) / step);
                if (k >= window) k = window - 1;
                float x = pl.Right - k * step;
                using (Pen hp = new Pen(Color.FromArgb(170, 210, 210, 210))) g.DrawLine(hp, x, pl.Top, x, pl.Bottom);
                List<string> rows = new List<string>(); List<Color> cols = new List<Color>();
                rows.Add(k == 0 ? "agora" : "-" + Secs(k * PeriodMs / 1000.0)); cols.Add(Color.Yellow);
                foreach (Series s in Items)
                    if (k < s.V.Count) { rows.Add(s.Sn.Name + ": " + Num(s.V[s.V.Count - 1 - k]) + " " + Sn.Unit(s.Sn.Type)); cols.Add(s.Color); }
                DrawBox(g, pl, (int)x + 10, pl.Bottom - rows.Count * 14 - 12, rows, cols);
            }
            if (Paused) TextRenderer.DrawText(g, Lang.T("PAUSADO"), Xp.UiBold, new Point(pl.Right - 70, pl.Top + 4), Color.Yellow);
            if (Items.Count == 0)
                TextRenderer.DrawText(g, Lang.T("Marque sensores na lista (ou use 'Essenciais') para ver o gráfico"), Font, new Point(pl.Left + 20, pl.Top + pl.Height / 2), Xp.GraphText);
        }
    }

    // ------------------------------------------------------------------
    //  NVML (biblioteca oficial da NVIDIA, ja vem com o driver)
    // ------------------------------------------------------------------
    static class Nvml
    {
        const string DLL = "nvml.dll";
        const CallingConvention CC = CallingConvention.Cdecl;
        public static bool Available;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string f);

        [StructLayout(LayoutKind.Sequential)] struct FanRpmInfo { public uint version; public uint fan; public uint speed; }
        [StructLayout(LayoutKind.Sequential)] struct MemInfo { public ulong total; public ulong free; public ulong used; }
        [StructLayout(LayoutKind.Sequential)] struct UtilInfo { public uint gpu; public uint memory; }
        [StructLayout(LayoutKind.Sequential)] struct Bar1Info { public ulong total; public ulong free; public ulong used; }
        [StructLayout(LayoutKind.Sequential)] struct ClkOff { public uint version; public int type; public int pstate; public int offset; public int min; public int max; }

        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlInit_v2();
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlShutdown();
        [DllImport(DLL, CallingConvention = CC)] static extern IntPtr nvmlErrorString(int r);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlSystemGetDriverVersion(StringBuilder s, uint len);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetCount_v2(out uint c);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetHandleByIndex_v2(uint i, out IntPtr h);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetName(IntPtr h, StringBuilder s, uint len);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetUUID(IntPtr h, StringBuilder s, uint len);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetVbiosVersion(IntPtr h, StringBuilder s, uint len);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetTemperature(IntPtr h, int sensor, out uint t);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetTemperatureThreshold(IntPtr h, int type, out uint t);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetTemperatureThreshold(IntPtr h, int type, ref int t);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetFanSpeed_v2(IntPtr h, uint fan, out uint s);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetNumFans(IntPtr h, out uint n);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMinMaxFanSpeed(IntPtr h, out uint min, out uint max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetFanSpeed_v2(IntPtr h, uint fan, uint speed);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetDefaultFanSpeed_v2(IntPtr h, uint fan);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetFanControlPolicy(IntPtr h, uint fan, uint policy);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetFanSpeedRPM(IntPtr h, ref FanRpmInfo i);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetClockInfo(IntPtr h, int type, out uint mhz);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMaxClockInfo(IntPtr h, int type, out uint mhz);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetPowerUsage(IntPtr h, out uint mw);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetPowerManagementLimit(IntPtr h, out uint mw);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr h, out uint mw);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetPowerManagementLimitConstraints(IntPtr h, out uint min, out uint max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetPowerManagementLimit(IntPtr h, uint mw);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMemoryInfo(IntPtr h, ref MemInfo m);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetUtilizationRates(IntPtr h, ref UtilInfo u);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMemoryBusWidth(IntPtr h, out uint w);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetNumGpuCores(IntPtr h, out uint n);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetArchitecture(IntPtr h, out uint a);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetCudaComputeCapability(IntPtr h, out int major, out int minor);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetPerformanceState(IntPtr h, out int p);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetCurrPcieLinkGeneration(IntPtr h, out uint v);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetCurrPcieLinkWidth(IntPtr h, out uint v);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMaxPcieLinkGeneration(IntPtr h, out uint v);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMaxPcieLinkWidth(IntPtr h, out uint v);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetGpcClkVfOffset(IntPtr h, out int o);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetGpcClkVfOffset(IntPtr h, int o);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMemClkVfOffset(IntPtr h, out int o);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetMemClkVfOffset(IntPtr h, int o);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetGpcClkMinMaxVfOffset(IntPtr h, out int min, out int max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetMemClkMinMaxVfOffset(IntPtr h, out int min, out int max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetGpuLockedClocks(IntPtr h, uint min, uint max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceResetGpuLockedClocks(IntPtr h);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetMemoryLockedClocks(IntPtr h, uint min, uint max);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceResetMemoryLockedClocks(IntPtr h);

        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetClockOffsets(IntPtr h, ref ClkOff i);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceSetClockOffsets(IntPtr h, ref ClkOff i);

        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetBAR1MemoryInfo(IntPtr h, ref Bar1Info b);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetDisplayActive(IntPtr h, out int a);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetEccMode(IntPtr h, out int cur, out int pend);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetComputeMode(IntPtr h, out int m);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetGspFirmwareMode(IntPtr h, out uint en, out uint def);
        [DllImport(DLL, CallingConvention = CC)] static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr h, out ulong r);

        delegate int GetU(IntPtr h, out uint v);
        delegate int GetI(IntPtr h, out int v);
        delegate int GetS(IntPtr h, StringBuilder sb, uint len);

        static uint? U(GetU f, IntPtr h) { try { uint v; if (f(h, out v) == 0) return v; } catch (Exception) { } return null; }
        static int? I(GetI f, IntPtr h) { try { int v; if (f(h, out v) == 0) return v; } catch (Exception) { } return null; }
        static string S(GetS f, IntPtr h) { try { StringBuilder sb = new StringBuilder(128); if (f(h, sb, 128) == 0) return sb.ToString(); } catch (Exception) { } return null; }
        static int Run(Func<int> f)
        {
            try { return f(); }
            catch (EntryPointNotFoundException) { return -999; }
            catch (DllNotFoundException) { return -998; }
        }

        public static string Err(int code)
        {
            if (code == -999) return "recurso inexistente neste driver";
            if (code == -998) return "nvml.dll não encontrada";
            try { return Marshal.PtrToStringAnsi(nvmlErrorString(code)) + " (" + code + ")"; } catch (Exception) { return "erro " + code; }
        }

        public static bool Init()
        {
            try
            {
                string[] paths = { Path.Combine(Environment.SystemDirectory, "nvml.dll"), @"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll" };
                foreach (string p in paths) if (File.Exists(p) && LoadLibrary(p) != IntPtr.Zero) break;
                Available = nvmlInit_v2() == 0;
            }
            catch (Exception) { Available = false; }
            return Available;
        }
        public static void Shutdown() { if (Available) { try { nvmlShutdown(); } catch (Exception) { } } }

        public static string Driver() { try { StringBuilder sb = new StringBuilder(80); if (nvmlSystemGetDriverVersion(sb, 80) == 0) return sb.ToString(); } catch (Exception) { } return null; }
        public static List<IntPtr> Devices()
        {
            List<IntPtr> l = new List<IntPtr>();
            if (!Available) return l;
            try
            {
                uint n;
                if (nvmlDeviceGetCount_v2(out n) != 0) return l;
                for (uint i = 0; i < n; i++) { IntPtr h; if (nvmlDeviceGetHandleByIndex_v2(i, out h) == 0) l.Add(h); }
            }
            catch (Exception) { }
            return l;
        }
        public static string Name(IntPtr h) { return S(nvmlDeviceGetName, h); }
        public static string Uuid(IntPtr h) { return S(nvmlDeviceGetUUID, h); }
        public static string Vbios(IntPtr h) { return S(nvmlDeviceGetVbiosVersion, h); }

        public static uint? Temp(IntPtr h) { return U(delegate(IntPtr x, out uint o) { return nvmlDeviceGetTemperature(x, 0, out o); }, h); }
        public static uint? TempThr(IntPtr h, int type) { return U(delegate(IntPtr x, out uint o) { return nvmlDeviceGetTemperatureThreshold(x, type, out o); }, h); }
        public static int SetTempThr(IntPtr h, int type, int t) { int v = t; return Run(delegate { return nvmlDeviceSetTemperatureThreshold(h, type, ref v); }); }

        public static uint? Fan(IntPtr h, uint fan) { return U(delegate(IntPtr x, out uint o) { return nvmlDeviceGetFanSpeed_v2(x, fan, out o); }, h); }
        public static uint? NumFans(IntPtr h) { return U(nvmlDeviceGetNumFans, h); }
        public static bool MinMaxFan(IntPtr h, out uint mn, out uint mx)
        {
            mn = 0; mx = 100;
            try { uint a, b; if (nvmlDeviceGetMinMaxFanSpeed(h, out a, out b) == 0) { mn = a; mx = b; return true; } } catch (Exception) { }
            return false;
        }
        public static uint? FanRpm(IntPtr h, uint fan)
        {
            try
            {
                FanRpmInfo i = new FanRpmInfo();
                i.version = (uint)Marshal.SizeOf(typeof(FanRpmInfo)) | (1u << 24);
                i.fan = fan;
                if (nvmlDeviceGetFanSpeedRPM(h, ref i) == 0) return i.speed;
            }
            catch (Exception) { }
            return null;
        }
        public static int SetFan(IntPtr h, uint fan, uint pct) { return Run(delegate { return nvmlDeviceSetFanSpeed_v2(h, fan, pct); }); }
        public static int SetFanDefault(IntPtr h, uint fan) { return Run(delegate { return nvmlDeviceSetDefaultFanSpeed_v2(h, fan); }); }
        public static int SetFanPolicy(IntPtr h, uint fan, uint policy) { return Run(delegate { return nvmlDeviceSetFanControlPolicy(h, fan, policy); }); }

        // tipos: 0 = grafico, 1 = SM, 2 = memoria, 3 = video
        public static uint? Clock(IntPtr h, int type) { return U(delegate(IntPtr x, out uint o) { return nvmlDeviceGetClockInfo(x, type, out o); }, h); }
        public static uint? MaxClock(IntPtr h, int type) { return U(delegate(IntPtr x, out uint o) { return nvmlDeviceGetMaxClockInfo(x, type, out o); }, h); }

        public static uint? Power(IntPtr h) { return U(nvmlDeviceGetPowerUsage, h); }
        public static uint? PowerLimit(IntPtr h) { return U(nvmlDeviceGetPowerManagementLimit, h); }
        public static uint? PowerDefault(IntPtr h) { return U(nvmlDeviceGetPowerManagementDefaultLimit, h); }
        public static bool PowerRange(IntPtr h, out uint mn, out uint mx)
        {
            mn = 0; mx = 0;
            try { uint a, b; if (nvmlDeviceGetPowerManagementLimitConstraints(h, out a, out b) == 0) { mn = a; mx = b; return true; } } catch (Exception) { }
            return false;
        }
        public static int SetPower(IntPtr h, uint mw) { return Run(delegate { return nvmlDeviceSetPowerManagementLimit(h, mw); }); }

        public static bool Mem(IntPtr h, out ulong total, out ulong used)
        {
            total = 0; used = 0;
            try { MemInfo m = new MemInfo(); if (nvmlDeviceGetMemoryInfo(h, ref m) == 0) { total = m.total; used = m.used; return true; } } catch (Exception) { }
            return false;
        }
        public static bool GetUtil(IntPtr h, out uint gpu, out uint mem)
        {
            gpu = 0; mem = 0;
            try { UtilInfo u = new UtilInfo(); if (nvmlDeviceGetUtilizationRates(h, ref u) == 0) { gpu = u.gpu; mem = u.memory; return true; } } catch (Exception) { }
            return false;
        }
        public static uint? MemBus(IntPtr h) { return U(nvmlDeviceGetMemoryBusWidth, h); }
        public static uint? Cores(IntPtr h) { return U(nvmlDeviceGetNumGpuCores, h); }
        public static int? Pstate(IntPtr h) { return I(nvmlDeviceGetPerformanceState, h); }
        public static string Arch(IntPtr h)
        {
            uint? a = U(nvmlDeviceGetArchitecture, h);
            if (!a.HasValue) return null;
            switch (a.Value)
            {
                case 2: return "Kepler"; case 3: return "Maxwell"; case 4: return "Pascal"; case 5: return "Volta";
                case 6: return "Turing"; case 7: return "Ampere"; case 8: return "Ada Lovelace"; case 9: return "Hopper";
                case 10: return "Blackwell"; default: return "Código " + a.Value;
            }
        }
        public static string Compute(IntPtr h)
        {
            try { int a, b; if (nvmlDeviceGetCudaComputeCapability(h, out a, out b) == 0) return a + "." + b; } catch (Exception) { }
            return null;
        }
        public static uint? Pcie(IntPtr h, bool width, bool max)
        {
            if (!width && !max) return U(nvmlDeviceGetCurrPcieLinkGeneration, h);
            if (width && !max) return U(nvmlDeviceGetCurrPcieLinkWidth, h);
            if (!width) return U(nvmlDeviceGetMaxPcieLinkGeneration, h);
            return U(nvmlDeviceGetMaxPcieLinkWidth, h);
        }
        public static bool Bar1(IntPtr h, out ulong total, out ulong used)
        {
            total = 0; used = 0;
            try { Bar1Info b = new Bar1Info(); if (nvmlDeviceGetBAR1MemoryInfo(h, ref b) == 0) { total = b.total; used = b.used; return true; } } catch (Exception) { }
            return false;
        }
        public static int? DisplayActive(IntPtr h) { return I(nvmlDeviceGetDisplayActive, h); }
        public static int? ComputeMode(IntPtr h) { return I(nvmlDeviceGetComputeMode, h); }
        public static int? EccCurrent(IntPtr h)
        {
            try { int c, p; if (nvmlDeviceGetEccMode(h, out c, out p) == 0) return c; } catch (Exception) { }
            return null;
        }
        public static int? Gsp(IntPtr h)
        {
            try { uint e, d; if (nvmlDeviceGetGspFirmwareMode(h, out e, out d) == 0) return (int)e; } catch (Exception) { }
            return null;
        }
        public static string Throttle(IntPtr h)
        {
            try
            {
                ulong r;
                if (nvmlDeviceGetCurrentClocksThrottleReasons(h, out r) != 0) return "Indisponível";
                if (r == 0) return "Nenhuma";
                List<string> l = new List<string>();
                if ((r & 0x1) != 0) l.Add("ociosa (normal)");
                if ((r & 0x2) != 0) l.Add("clock de aplicativo");
                if ((r & 0x4) != 0) l.Add("limite de potência (SW)");
                if ((r & 0x8) != 0) l.Add("slowdown de hardware");
                if ((r & 0x20) != 0) l.Add("limite térmico (SW)");
                if ((r & 0x40) != 0) l.Add("limite térmico (HW)");
                if ((r & 0x80) != 0) l.Add("freio de energia (HW)");
                return l.Count == 0 ? "Código " + r : string.Join(", ", l.ToArray());
            }
            catch (Exception) { return "Indisponível"; }
        }

        // API nova (R535+): offsets por tipo de clock (0 = nucleo, 2 = memoria) no estado P0
        public static bool OffNew(IntPtr h, int type, out int cur, out int mn, out int mx)
        {
            cur = 0; mn = 0; mx = 0;
            try
            {
                ClkOff c = new ClkOff();
                c.version = (uint)Marshal.SizeOf(typeof(ClkOff)) | (1u << 24);
                c.type = type; c.pstate = 0;
                if (nvmlDeviceGetClockOffsets(h, ref c) == 0) { cur = c.offset; mn = c.min; mx = c.max; return true; }
            }
            catch (Exception) { }
            return false;
        }
        public static int SetOffsetNew(IntPtr h, int type, int mhz)
        {
            return Run(delegate
            {
                ClkOff c = new ClkOff();
                c.version = (uint)Marshal.SizeOf(typeof(ClkOff)) | (1u << 24);
                c.type = type; c.pstate = 0; c.offset = mhz;
                return nvmlDeviceSetClockOffsets(h, ref c);
            });
        }
        public static int? GpcOffset(IntPtr h)
        {
            int? v = I(nvmlDeviceGetGpcClkVfOffset, h);
            if (v.HasValue) return v;
            int c, a, b;
            return OffNew(h, 0, out c, out a, out b) ? (int?)c : null;
        }
        public static int? MemOffset(IntPtr h)
        {
            int? v = I(nvmlDeviceGetMemClkVfOffset, h);
            if (v.HasValue) return v;
            int c, a, b;
            return OffNew(h, 2, out c, out a, out b) ? (int?)c : null;
        }
        public static bool GpcRange(IntPtr h, out int mn, out int mx)
        {
            mn = -500; mx = 500;
            try { int a, b; if (nvmlDeviceGetGpcClkMinMaxVfOffset(h, out a, out b) == 0 && b > a) { mn = a; mx = b; return true; } } catch (Exception) { }
            int c, x, y;
            if (OffNew(h, 0, out c, out x, out y) && y > x) { mn = x; mx = y; return true; }
            return false;
        }
        public static bool MemRange(IntPtr h, out int mn, out int mx)
        {
            mn = -1000; mx = 1500;
            try { int a, b; if (nvmlDeviceGetMemClkMinMaxVfOffset(h, out a, out b) == 0 && b > a) { mn = a; mx = b; return true; } } catch (Exception) { }
            int c, x, y;
            if (OffNew(h, 2, out c, out x, out y) && y > x) { mn = x; mx = y; return true; }
            return false;
        }
        public static int SetGpcOffset(IntPtr h, int o) { return Run(delegate { return nvmlDeviceSetGpcClkVfOffset(h, o); }); }
        public static int SetMemOffset(IntPtr h, int o) { return Run(delegate { return nvmlDeviceSetMemClkVfOffset(h, o); }); }
        public static int LockGpu(IntPtr h, uint a, uint b) { return Run(delegate { return nvmlDeviceSetGpuLockedClocks(h, a, b); }); }
        public static int UnlockGpu(IntPtr h) { return Run(delegate { return nvmlDeviceResetGpuLockedClocks(h); }); }
        public static int LockMem(IntPtr h, uint a, uint b) { return Run(delegate { return nvmlDeviceSetMemoryLockedClocks(h, a, b); }); }
        public static int UnlockMem(IntPtr h) { return Run(delegate { return nvmlDeviceResetMemoryLockedClocks(h); }); }
    }

    // ------------------------------------------------------------------
    //  NvAPI (Pstates20) - alternativa quando o NVML recusa o offset de clock
    // ------------------------------------------------------------------
    static class NvApi
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface(uint id);
        delegate int D0();
        delegate int DEnum(IntPtr handles, ref uint count);
        delegate int DSet(IntPtr gpu, IntPtr info);
        static bool ready;
        static DEnum enumGpu;
        static DSet setP20;
        const int INFO_SIZE = 7416;   // NV_GPU_PERF_PSTATES20_INFO_V2

        static bool Init(out string err)
        {
            err = null;
            if (ready) return true;
            try
            {
                IntPtr pi = QueryInterface(0x0150E828);
                if (pi == IntPtr.Zero) { err = "NvAPI indisponível"; return false; }
                D0 init = (D0)Marshal.GetDelegateForFunctionPointer(pi, typeof(D0));
                int r = init();
                if (r != 0) { err = "NvAPI_Initialize erro " + r; return false; }
                IntPtr pe = QueryInterface(0xE5AC921F), ps = QueryInterface(0x0F4DAE6B);
                if (pe == IntPtr.Zero || ps == IntPtr.Zero) { err = "NvAPI sem SetPstates20"; return false; }
                enumGpu = (DEnum)Marshal.GetDelegateForFunctionPointer(pe, typeof(DEnum));
                setP20 = (DSet)Marshal.GetDelegateForFunctionPointer(ps, typeof(DSet));
                ready = true;
                return true;
            }
            catch (Exception ex) { err = "NvAPI: " + ex.Message; return false; }
        }

        static string Name(int r)
        {
            switch (r)
            {
                case -1: return "erro genérico"; case -3: return "sem implementação"; case -5: return "argumento inválido";
                case -9: return "versão de estrutura incompatível"; case -104: return "não suportado nesta GPU/notebook";
                case -6: return "sem driver NVIDIA"; case -8: return "GPU não encontrada"; case -110: return "acesso negado (rode como administrador)";
                default: return "código " + r;
            }
        }

        // domainId: 0 = nucleo, 4 = memoria. Retorna null se deu certo, ou o erro.
        public static string SetOffset(int gpuIndex, int domainId, int deltaKHz)
        {
            string err;
            if (!Init(out err)) return err;
            IntPtr handles = Marshal.AllocHGlobal(64 * IntPtr.Size);
            IntPtr info = Marshal.AllocHGlobal(INFO_SIZE);
            try
            {
                for (int i = 0; i < 64 * IntPtr.Size; i++) Marshal.WriteByte(handles, i, 0);
                for (int i = 0; i < INFO_SIZE; i += 8) Marshal.WriteInt64(info, i, 0);
                uint cnt = 0;
                int r = enumGpu(handles, ref cnt);
                if (r != 0 || gpuIndex >= (int)cnt) return "NvAPI EnumGPUs erro " + r;
                IntPtr gpu = Marshal.ReadIntPtr(handles, gpuIndex * IntPtr.Size);
                Marshal.WriteInt32(info, 0, INFO_SIZE | (2 << 16)); // versao 2
                Marshal.WriteInt32(info, 8, 1);                      // numPstates
                Marshal.WriteInt32(info, 12, 1);                     // numClocks
                Marshal.WriteInt32(info, 16, 0);                     // numBaseVoltages
                int pst = 20;                                        // pstates[0] = P0
                Marshal.WriteInt32(info, pst, 0);
                int ck = pst + 8;                                    // clocks[0]
                Marshal.WriteInt32(info, ck, domainId);
                Marshal.WriteInt32(info, ck + 4, 0);                 // tipo "single"
                Marshal.WriteInt32(info, ck + 12, deltaKHz);         // freqDelta_kHz.value
                r = setP20(gpu, info);
                if (r == -9 || r == -5)
                {
                    // driver mais antigo: tenta a estrutura versao 1
                    Marshal.WriteInt32(info, 0, 7316 | (1 << 16));
                    r = setP20(gpu, info);
                }
                return r == 0 ? null : "NvAPI SetPstates20 erro " + r + " (" + Name(r) + ")";
            }
            catch (Exception ex) { return "NvAPI: " + ex.Message; }
            finally { Marshal.FreeHGlobal(handles); Marshal.FreeHGlobal(info); }
        }
    }

    // ------------------------------------------------------------------
    //  Sensor generico (vem do LibreHardwareMonitor ou do NVML)
    // ------------------------------------------------------------------
    class Sn
    {
        public string Group, Name, Type;
        public Func<float?> Get;
        public LHM.ISensor Ls;
        public bool IsGpu;
        public string Area
        {
            get
            {
                if (IsGpu) return "GPU";
                if (Group.StartsWith("Processador")) return "CPU";
                if (Group.StartsWith("Placa-mae")) return "Placa-mae";
                if (Group.StartsWith("Memória RAM")) return "Memória RAM";
                if (Group.StartsWith("Armazenamento")) return "Discos";
                if (Group.StartsWith("Cooler")) return "Coolers";
                if (Group.StartsWith("Rede")) return "Rede";
                return "Outros";
            }
        }
        public bool InArea(string a) { if (a == "Ventoinhas") return Type == "Fan" || Type == "Control"; return Area == a; }
        // exclui "distancia ate o TjMax", limites e alarmes (nao sao temperaturas reais)
        public bool IsRealTemp
        {
            get
            {
                if (Type != "Temperature") return false;
                string n = Name.ToLower();
                return n.IndexOf("distance") < 0 && n.IndexOf("tjmax") < 0 && n.IndexOf("limit") < 0 && n.IndexOf("critical") < 0 &&
                       n.IndexOf("warning") < 0 && n.IndexOf("target") < 0 && n.IndexOf("threshold") < 0;
            }
        }
        object lastBox;
        public float? Last { get { object o = lastBox; return o == null ? null : (float?)(float)o; } }
        public float Min, Max;
        bool seen;
        public bool Seen { get { return seen; } }
        public float? AlertAbove;
        public DateTime LastBeep = DateTime.MinValue;
        // chave estavel (sem acentos): configuracoes salvas por versoes antigas continuam valendo
        string kG, kN, kK;
        public string Key
        {
            get
            {
                if (kK != null && object.ReferenceEquals(Group, kG) && object.ReferenceEquals(Name, kN)) return kK;
                kG = Group; kN = Name; kK = NoAcc(Group) + "~:~" + NoAcc(Name); return kK;
            }
        }
        static string NoAcc(string t)
        {
            if (string.IsNullOrEmpty(t)) return t;
            string d = t.Normalize(NormalizationForm.FormD); StringBuilder sb = new StringBuilder(d.Length);
            foreach (char c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
        public void ResetMinMax() { float? l = Last; if (l.HasValue) { Min = l.Value; Max = l.Value; } }
        public string Category
        {
            get
            {
                switch (Type)
                {
                    case "Temperature": return "Temperatura";
                    case "Load": case "Level": return "Uso (%)";
                    case "Clock": case "Frequency": return "Clock";
                    case "Power": case "Energy": return "Potência";
                    case "Voltage": return "Voltagem";
                    case "Current": return "Corrente";
                    case "Fan": return "Ventoinha";
                    case "Control": return "Controle (%)";
                    case "Data": case "SmallData": return "Memória/Dados";
                    case "Throughput": return "Taxa de dados";
                    default: return "Outros";
                }
            }
        }
        public void Poll()
        {
            float? v = null;
            try { v = Get(); } catch (Exception) { }
            if (v.HasValue && (float.IsNaN(v.Value) || float.IsInfinity(v.Value))) v = null;
            // leituras impossiveis (driver sem acesso ao sensor): 0 C / 0 V / temperatura absurda -> sem leitura
            if (v.HasValue && Type == "Temperature" && (v.Value <= 0f || v.Value > 150f)) v = null;
            if (v.HasValue && Type == "Voltage" && v.Value == 0f) v = null;
            lastBox = v.HasValue ? (object)v.Value : null;
            if (!v.HasValue) return;
            if (!seen) { Min = Max = v.Value; seen = true; }
            else { Min = Math.Min(Min, v.Value); Max = Math.Max(Max, v.Value); }
        }
        public static string Unit(string t)
        {
            switch (t)
            {
                case "Temperature": return "\u00B0C";
                case "Load": case "Control": case "Level": return "%";
                case "Fan": return "RPM";
                case "Clock": return "MHz";
                case "Frequency": return "Hz";
                case "Power": return "W";
                case "Voltage": return "V";
                case "Current": return "A";
                case "Data": return "GB";
                case "SmallData": return "MB";
                case "Throughput": return "B/s";
                case "Flow": return "L/h";
                case "Energy": return "mWh";
                case "Noise": return "dBA";
                case "TimeSpan": return "s";
                default: return "";
            }
        }
        public string Fmt(float? v)
        {
            if (!v.HasValue) return "-";
            return v.Value.ToString("0.##") + " " + Unit(Type);
        }
    }

    // ------------------------------------------------------------------
    //  Uma placa de video
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    //  Overclock / ventoinha por fabricante: NVIDIA (NVML+NvAPI), AMD (ADL Overdrive8) e Intel (IGCL)
    // ------------------------------------------------------------------
    class OcCap
    {
        public bool Ok;
        public int Min, Max, Cur, Def;
        public string Unit = "", Why = "";
    }

    abstract class GpuOc
    {
        public string Backend = "", Note = "";
        public OcCap Core = new OcCap(), Mem = new OcCap(), Power = new OcCap(), Temp = new OcCap();
        public bool CanLock, CanFan;
        public abstract void Refresh();
        public abstract string SetCore(int v);
        public abstract string SetMem(int v);
        public abstract string SetPower(int v);
        public abstract string SetTemp(int v);
        public abstract string ResetAll();
        public virtual string SetFan(int pct) { return "Controle de ventoinha indisponível."; }
        public virtual string FanAuto() { return "Controle de ventoinha indisponível."; }
        public virtual int? FanPct() { return null; }
        public virtual int? FanRpm() { return null; }
        public static bool IsOk(string s) { return s != null && s.StartsWith("OK"); }
    }

    // ---------- NVIDIA (desktop e notebook) ----------
    class NvOc : GpuOc
    {
        Gpu g; uint defPowerMw; int defTemp;
        public NvOc(Gpu gpu)
        {
            g = gpu; Backend = "NVIDIA (NVML + NvAPI)"; CanLock = true; CanFan = true;
        }
        public override void Refresh()
        {
            int mn, mx;
            bool known = Nvml.GpcRange(g.H, out mn, out mx);
            Core = new OcCap(); Core.Ok = true; Core.Unit = "MHz"; Core.Min = mn; Core.Max = mx; Core.Cur = Nvml.GpcOffset(g.H) ?? 0;
            if (!known) Core.Why = "o driver não informa a faixa (comum em notebooks); valores acima do limite serão recusados";
            known = Nvml.MemRange(g.H, out mn, out mx);
            Mem = new OcCap(); Mem.Ok = true; Mem.Unit = "MHz"; Mem.Min = mn / Gpu.MemFactor; Mem.Max = mx / Gpu.MemFactor; Mem.Cur = (Nvml.MemOffset(g.H) ?? 0) / Gpu.MemFactor;
            if (!known) Mem.Why = "o driver não informa a faixa (comum em notebooks); valores acima do limite serão recusados";
            Power = new OcCap(); Power.Unit = "W";
            uint pmn, pmx;
            defPowerMw = Nvml.PowerDefault(g.H) ?? 0;
            if (Nvml.PowerRange(g.H, out pmn, out pmx) && pmx > pmn)
            {
                Power.Ok = true; Power.Min = (int)(pmn / 1000); Power.Max = (int)(pmx / 1000); Power.Def = (int)(defPowerMw / 1000);
                uint? pl = Nvml.PowerLimit(g.H);
                Power.Cur = pl.HasValue ? (int)(pl.Value / 1000) : Power.Max;
            }
            else Power.Why = "limite de potência fixo nesta placa (normal em notebooks: o fabricante trava o TDP)";
            Temp = new OcCap(); Temp.Unit = "\u00B0C";
            uint? tmin = Nvml.TempThr(g.H, 4), tmax = Nvml.TempThr(g.H, 6), tcur = Nvml.TempThr(g.H, 5);
            if (tcur.HasValue)
            {
                Temp.Ok = true; Temp.Min = tmin.HasValue ? (int)tmin.Value : 60; Temp.Max = tmax.HasValue ? (int)tmax.Value : 95;
                if ((int)tcur.Value > Temp.Max) Temp.Max = (int)tcur.Value;
                if ((int)tcur.Value < Temp.Min) Temp.Min = (int)tcur.Value;
                Temp.Cur = (int)tcur.Value; Temp.Def = Temp.Cur; defTemp = Temp.Cur;
            }
            else Temp.Why = "limite de temperatura não ajustável nesta placa";
        }
        static string Hint(string r)
        {
            if (r.StartsWith("FALHOU"))
                return r + "  |  Dica: em notebooks o fabricante (BIOS/vBIOS) pode bloquear o overclock; atualize o driver NVIDIA e tente valores menores. A seção \"Travar clock\" costuma funcionar mesmo assim.";
            return r;
        }
        public override string SetCore(int v)
        {
            string r = Hint(g.SetClockOffset(true, v));
            int? rc = Nvml.GpcOffset(g.H);
            return r.StartsWith("OK") && rc.HasValue ? r + "  [driver le " + rc.Value + "]" : r;
        }
        public override string SetMem(int v)
        {
            string r = Hint(g.SetClockOffset(false, v));
            int? rm = Nvml.MemOffset(g.H);
            return r.StartsWith("OK") && rm.HasValue ? r + "  [driver le " + (rm.Value / Gpu.MemFactor) + "]" : r;
        }
        public override string SetPower(int v)
        {
            if (!Power.Ok) return "não suportado nesta placa";
            int r = Nvml.SetPower(g.H, (uint)(v * 1000));
            return r == 0 ? "OK" : Nvml.Err(r);
        }
        public override string SetTemp(int v)
        {
            if (!Temp.Ok) return "não suportado nesta placa";
            int r = Nvml.SetTempThr(g.H, 5, v);
            return r == 0 ? "OK" : Nvml.Err(r);
        }
        public override string ResetAll()
        {
            g.SetClockOffset(true, 0); g.SetClockOffset(false, 0);
            if (defPowerMw > 0) Nvml.SetPower(g.H, defPowerMw);
            if (defTemp > 0) Nvml.SetTempThr(g.H, 5, defTemp);
            Nvml.UnlockGpu(g.H); Nvml.UnlockMem(g.H);
            return "OK";
        }
    }

    // ---------- AMD: biblioteca ADL (atiadlxx.dll, instalada junto do driver Radeon) ----------
    static class Adl
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string f);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr h, string n);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr DAlloc(int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DCreate(DAlloc cb, int enumConnected, out IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DNum(IntPtr ctx, out int n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DInfo(IntPtr ctx, IntPtr buf, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DCaps(IntPtr ctx, int ad, out int supported, out int enabled, out int version);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DPtr(IntPtr ctx, int ad, IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DPtr2(IntPtr ctx, int ad, IntPtr p, IntPtr q);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DNone(IntPtr ctx, int ad);

        static DAlloc alloc = delegate(int size) { return Marshal.AllocCoTaskMem(size); };
        static bool tried, ok;
        static IntPtr ctx;
        static DNum fNum; static DInfo fInfo; static DCaps fCaps;
        static DPtr fInitGet, fCurGet; static DPtr2 fSet;
        static DPtr fFanGet6, fFanSet6; static DNone fFanReset6;
        public static string Error = "";
        const int INFO_SIZE_DEFAULT = 1572;

        public class Ad { public int Index, Bus, Vendor, Od; public string Name = ""; }
        public static List<Ad> Adapters = new List<Ad>();
        static HashSet<int> usedAd = new HashSet<int>();

        static T Fn<T>(IntPtr lib, string name) where T : class
        {
            IntPtr p = GetProcAddress(lib, name);
            return p == IntPtr.Zero ? null : (Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T);
        }

        static bool Init()
        {
            if (tried) return ok;
            tried = true;
            try
            {
                IntPtr lib = LoadLibrary("atiadlxx.dll");
                if (lib == IntPtr.Zero) lib = LoadLibrary("atiadlxy.dll");
                if (lib == IntPtr.Zero) { Error = "atiadlxx.dll não encontrada (instale o driver Radeon/Adrenalin)"; return false; }
                DCreate create = Fn<DCreate>(lib, "ADL2_Main_Control_Create");
                fNum = Fn<DNum>(lib, "ADL2_Adapter_NumberOfAdapters_Get");
                fInfo = Fn<DInfo>(lib, "ADL2_Adapter_AdapterInfo_Get");
                fCaps = Fn<DCaps>(lib, "ADL2_Overdrive_Caps");
                fInitGet = Fn<DPtr>(lib, "ADL2_Overdrive8_Init_Setting_Get");
                fCurGet = Fn<DPtr>(lib, "ADL2_Overdrive8_Current_Setting_Get");
                fSet = Fn<DPtr2>(lib, "ADL2_Overdrive8_Setting_Set");
                fFanGet6 = Fn<DPtr>(lib, "ADL2_Overdrive6_FanSpeed_Get");
                fFanSet6 = Fn<DPtr>(lib, "ADL2_Overdrive6_FanSpeed_Set");
                fFanReset6 = Fn<DNone>(lib, "ADL2_Overdrive6_FanSpeed_Reset");
                if (create == null || fNum == null || fInfo == null) { Error = "ADL incompleta"; return false; }
                int r = create(alloc, 0, out ctx);   // 0 = tambem adaptadores sem monitor ligado
                if (r != 0 || ctx == IntPtr.Zero) { ctx = IntPtr.Zero; r = create(alloc, 1, out ctx); }
                if (r != 0 || ctx == IntPtr.Zero) { Error = "ADL2_Main_Control_Create erro " + r; return false; }
                int n;
                if (fNum(ctx, out n) != 0 || n <= 0) { Error = "nenhum adaptador ADL"; return false; }
                int stride = INFO_SIZE_DEFAULT;
                StringBuilder seen = new StringBuilder();
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    int total = n * stride + 8192;
                    IntPtr buf = Marshal.AllocHGlobal(total);
                    try
                    {
                        for (int i = 0; i + 8 <= total; i += 8) Marshal.WriteInt64(buf, i, 0);
                        int ir = fInfo(ctx, buf, n * stride);
                        if (ir != 0) { Error = "AdapterInfo_Get falhou (" + ir + ")"; return false; }
                        int sz = Marshal.ReadInt32(buf, 0);   // iSize da propria estrutura
                        if (attempt == 0 && sz >= 1000 && sz <= 4000 && sz != stride) { stride = sz; continue; }
                        HashSet<int> seenBus = new HashSet<int>();
                        for (int i = 0; i < n; i++)
                        {
                            int bo = i * stride;
                            Ad a = new Ad();
                            a.Index = Marshal.ReadInt32(buf, bo + 4);
                            a.Bus = Marshal.ReadInt32(buf, bo + 8 + 256);
                            a.Vendor = Marshal.ReadInt32(buf, bo + 8 + 256 + 12);
                            a.Name = Marshal.PtrToStringAnsi(new IntPtr(buf.ToInt64() + bo + 8 + 256 + 16));
                            if (a.Name == null) a.Name = "";
                            if (seen.Length < 300) seen.Append(" [" + a.Vendor.ToString("X") + " " + a.Name + "]");
                            bool amd = a.Vendor == 0x1002 || a.Name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0 || a.Name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!amd) continue;
                            if (!seenBus.Add(a.Bus * 1000 + a.Index / 1000)) continue;
                            int sup, en, ver;
                            if (fCaps != null && fCaps(ctx, a.Index, out sup, out en, out ver) == 0) a.Od = sup != 0 ? ver : 0;
                            Adapters.Add(a);
                        }
                        break;
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                if (Adapters.Count == 0) Error = "nenhum adaptador AMD no ADL (" + n + " adaptador(es):" + seen + ")";
                ok = Adapters.Count > 0;
            }
            catch (Exception ex) { Error = "ADL: " + ex.Message; ok = false; }
            return ok;
        }

        static string Norm(string s) { return Regex.Replace((s ?? "").ToLower().Replace("amd", "").Replace("radeon", "").Replace("(tm)", "").Replace("graphics", ""), "[^a-z0-9]", ""); }

        public static GpuOc Attach(Gpu g, out string why)
        {
            why = "";
            if (!Init()) { why = Error; return null; }
            Ad pick = null;
            string n = Norm(g.Name);
            foreach (Ad a in Adapters)
                if (!usedAd.Contains(a.Index) && n.Length > 0 && (Norm(a.Name).Contains(n) || n.Contains(Norm(a.Name)))) { pick = a; break; }
            if (pick == null) foreach (Ad a in Adapters) if (!usedAd.Contains(a.Index)) { pick = a; break; }
            if (pick == null) { why = "adaptador AMD não encontrado no ADL"; return null; }
            usedAd.Add(pick.Index);
            AmdOc oc = new AmdOc(pick);
            oc.Refresh();
            return oc;
        }

        // ----- Overdrive8 (RDNA 1/2/3/4) -----
        public const int ID_GFX_MAX = 0, ID_UCLK_MAX = 8, ID_POWER = 9, ID_FAN_MIN = 10, ID_TEMP_MAX = 13, ID_FAN_ZERO = 15;
        const int BUF = 4096;

        public class Od8
        {
            public int Count;
            public int[] Min = new int[64], Max = new int[64], Def = new int[64], Cur = new int[64];
        }

        public static string ReadOd8(Ad a, out Od8 o)
        {
            o = new Od8();
            if (fInitGet == null || fCurGet == null) return "ADL sem Overdrive8";
            IntPtr init = Marshal.AllocHGlobal(BUF), cur = Marshal.AllocHGlobal(BUF);
            try
            {
                int[] tryCounts = { 0, 40, 37, 36, 33, 48, 64 };
                int rc = -1, cnt = 0;
                foreach (int tc in tryCounts)
                {
                    for (int i = 0; i < BUF; i += 8) Marshal.WriteInt64(init, i, 0);
                    Marshal.WriteInt32(init, 0, tc);
                    rc = fInitGet(ctx, a.Index, init);
                    cnt = Marshal.ReadInt32(init, 0);
                    if (rc == 0 && cnt >= 10 && cnt <= 100) break;
                    rc = rc == 0 ? -2 : rc;
                }
                if (rc != 0) return "Overdrive8_Init_Setting_Get erro " + rc;
                o.Count = Math.Min(cnt, 60);
                for (int i = 0; i < BUF; i += 8) Marshal.WriteInt64(cur, i, 0);
                Marshal.WriteInt32(cur, 0, cnt);
                int rc2 = fCurGet(ctx, a.Index, cur);
                for (int i = 0; i < o.Count; i++)
                {
                    int b = 8 + i * 16;
                    o.Min[i] = Marshal.ReadInt32(init, b + 4); o.Max[i] = Marshal.ReadInt32(init, b + 8); o.Def[i] = Marshal.ReadInt32(init, b + 12);
                    o.Cur[i] = rc2 == 0 ? Marshal.ReadInt32(cur, 4 + i * 4) : o.Def[i];
                }
                return null;
            }
            catch (Exception ex) { return "ADL: " + ex.Message; }
            finally { Marshal.FreeHGlobal(init); Marshal.FreeHGlobal(cur); }
        }

        // altera varios ajustes de uma vez; sets = pares (id, valor)
        public static string SetOd8(Ad a, int count, int[] ids, int[] vals)
        {
            if (fSet == null) return "ADL sem Overdrive8_Setting_Set";
            IntPtr set = Marshal.AllocHGlobal(BUF), cur = Marshal.AllocHGlobal(BUF);
            try
            {
                for (int i = 0; i < BUF; i += 8) { Marshal.WriteInt64(set, i, 0); Marshal.WriteInt64(cur, i, 0); }
                Marshal.WriteInt32(set, 0, count);
                Marshal.WriteInt32(cur, 0, count);
                for (int i = 0; i < ids.Length; i++)
                {
                    Marshal.WriteInt32(set, 4 + ids[i] * 8, 1);
                    Marshal.WriteInt32(set, 8 + ids[i] * 8, vals[i]);
                }
                int rc = fSet(ctx, a.Index, set, cur);
                return rc == 0 ? null : "Overdrive8_Setting_Set erro " + rc;
            }
            catch (Exception ex) { return "ADL: " + ex.Message; }
            finally { Marshal.FreeHGlobal(set); Marshal.FreeHGlobal(cur); }
        }

        // ----- Overdrive6 (ventoinha de placas GCN / Polaris) -----
        public static bool HasFan6 { get { return fFanSet6 != null; } }
        public static string FanSet6(Ad a, int pct)
        {
            IntPtr p = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(p, 0, 1); Marshal.WriteInt32(p, 4, pct); Marshal.WriteInt32(p, 8, 0); Marshal.WriteInt32(p, 12, 0);
                int rc = fFanSet6(ctx, a.Index, p);
                return rc == 0 ? null : "Overdrive6_FanSpeed_Set erro " + rc;
            }
            catch (Exception ex) { return "ADL: " + ex.Message; }
            finally { Marshal.FreeHGlobal(p); }
        }
        public static string FanReset6(Ad a)
        {
            try { if (fFanReset6 == null) return "sem reset"; int rc = fFanReset6(ctx, a.Index); return rc == 0 ? null : "Overdrive6_FanSpeed_Reset erro " + rc; }
            catch (Exception ex) { return "ADL: " + ex.Message; }
        }
        public static int? FanGet6(Ad a)
        {
            if (fFanGet6 == null) return null;
            IntPtr p = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(p, 0, 1); Marshal.WriteInt32(p, 4, 0); Marshal.WriteInt32(p, 8, 0); Marshal.WriteInt32(p, 12, 0);
                if (fFanGet6(ctx, a.Index, p) == 0) return Marshal.ReadInt32(p, 4);
            }
            catch (Exception) { }
            finally { Marshal.FreeHGlobal(p); }
            return null;
        }
    }

    class AmdOc : GpuOc
    {
        Adl.Ad ad; Adl.Od8 od = new Adl.Od8(); bool od8;
        public AmdOc(Adl.Ad a) { ad = a; Backend = "AMD (ADL Overdrive" + (a.Od > 0 ? " " + a.Od : "") + ")"; }

        static OcCap Range(Adl.Od8 o, int id, bool offset, string unit)
        {
            OcCap c = new OcCap(); c.Unit = unit;
            if (id >= o.Count) return c;
            int mn = o.Min[id], mx = o.Max[id], df = o.Def[id], cu = o.Cur[id];
            if (mx <= mn) return c;
            c.Ok = true;
            if (offset) { c.Min = mn - df; c.Max = mx - df; c.Cur = cu - df; c.Def = 0; }
            else { c.Min = mn; c.Max = mx; c.Cur = cu; c.Def = df; }
            return c;
        }

        public override void Refresh()
        {
            string e = null;
            od8 = false;
            if (ad.Od == 8) { e = Adl.ReadOd8(ad, out od); od8 = e == null; }
            string why = ad.Od == 8 ? "o driver não liberou este ajuste (" + (e ?? "feature desligada no Adrenalin") + ")"
                : ad.Od == 0 ? "Overdrive indisponível nesta placa/driver (ative \"Ajuste manual\" no AMD Adrenalin)"
                : "esta placa usa Overdrive " + ad.Od + " (Polaris/Vega/antiga): ajuste de clocks só no AMD Adrenalin; a ventoinha funciona se o driver permitir";
            if (od8)
            {
                Core = Range(od, Adl.ID_GFX_MAX, true, "MHz");
                Mem = Range(od, Adl.ID_UCLK_MAX, true, "MHz");
                Power = Range(od, Adl.ID_POWER, false, "%");
                Temp = Range(od, Adl.ID_TEMP_MAX, false, "\u00B0C");
                OcCap fan = Range(od, Adl.ID_FAN_MIN, false, "%");
                CanFan = fan.Ok;
                Note = "Overdrive8: núcleo = clock máximo; memória = clock máximo (MHz reais); potência em % relativo ao padrão.";
            }
            else
            {
                Core = new OcCap(); Mem = new OcCap(); Power = new OcCap(); Temp = new OcCap();
                CanFan = ad.Od == 6 && Adl.HasFan6;
                Note = why;
            }
            if (!Core.Ok) Core.Why = why;
            if (!Mem.Ok) Mem.Why = why;
            if (!Power.Ok) Power.Why = why;
            if (!Temp.Ok) Temp.Why = why;
        }

        string One(int id, int value)
        {
            if (!od8) return "Falhou: Overdrive8 indisponível";
            string e = Adl.SetOd8(ad, od.Count, new int[] { id }, new int[] { value });
            return e == null ? "OK (ADL)" : "Falhou: " + e + " (confira se o ajuste manual esta liberado no AMD Adrenalin)";
        }
        public override string SetCore(int v) { return Core.Ok ? One(Adl.ID_GFX_MAX, od.Def[Adl.ID_GFX_MAX] + v) : "não suportado"; }
        public override string SetMem(int v) { return Mem.Ok ? One(Adl.ID_UCLK_MAX, od.Def[Adl.ID_UCLK_MAX] + v) : "não suportado"; }
        public override string SetPower(int v) { return Power.Ok ? One(Adl.ID_POWER, v) : "não suportado"; }
        public override string SetTemp(int v) { return Temp.Ok ? One(Adl.ID_TEMP_MAX, v) : "não suportado"; }

        public override string ResetAll()
        {
            if (!od8) return "Falhou";
            List<int> ids = new List<int>(), vals = new List<int>();
            int[] all = { Adl.ID_GFX_MAX, Adl.ID_UCLK_MAX, Adl.ID_POWER, Adl.ID_TEMP_MAX, Adl.ID_FAN_MIN };
            foreach (int id in all) if (id < od.Count && od.Max[id] > od.Min[id]) { ids.Add(id); vals.Add(od.Def[id]); }
            string e = Adl.SetOd8(ad, od.Count, ids.ToArray(), vals.ToArray());
            return e == null ? "OK" : "Falhou: " + e;
        }

        public override string SetFan(int pct)
        {
            if (od8 && od.Max[Adl.ID_FAN_MIN] > od.Min[Adl.ID_FAN_MIN])
            {
                int v = Math.Max(od.Min[Adl.ID_FAN_MIN], Math.Min(od.Max[Adl.ID_FAN_MIN], pct));
                string e = Adl.SetOd8(ad, od.Count, new int[] { Adl.ID_FAN_MIN }, new int[] { v });
                return e == null ? "OK (ADL Overdrive8): velocidade mínima da ventoinha em " + v + "% (a placa pode subir se esquentar)" : "Falhou: " + e;
            }
            if (ad.Od == 6 && Adl.HasFan6)
            {
                string e = Adl.FanSet6(ad, pct);
                return e == null ? "OK (ADL Overdrive6): ventoinha em " + pct + "%" : "Falhou: " + e;
            }
            return "Esta placa AMD não aceitou controle de ventoinha pelo ADL.";
        }
        public override string FanAuto()
        {
            if (od8 && od.Max[Adl.ID_FAN_MIN] > od.Min[Adl.ID_FAN_MIN])
            {
                string e = Adl.SetOd8(ad, od.Count, new int[] { Adl.ID_FAN_MIN }, new int[] { od.Def[Adl.ID_FAN_MIN] });
                return e == null ? "OK" : "Falhou: " + e;
            }
            if (ad.Od == 6) { string e = Adl.FanReset6(ad); return e == null ? "OK" : "Falhou: " + e; }
            return "Falhou";
        }
        public override int? FanPct()
        {
            if (ad.Od == 6) return Adl.FanGet6(ad);
            return null;
        }
    }

    // ---------- Intel Arc / Graphics: biblioteca IGCL (ControlLib.dll, instalada com o driver Intel) ----------
    static class Igcl
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string f);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr h, string n);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DInit(IntPtr args, out IntPtr api);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DEnum(IntPtr api, ref uint count, IntPtr devs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DProps(IntPtr dev, IntPtr props);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DDev(IntPtr dev);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DSetD(IntPtr dev, double v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DGetD(IntPtr dev, out double v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DFans(IntPtr dev, ref uint count, IntPtr fans);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DFanSet(IntPtr fan, IntPtr speed);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DFanDef(IntPtr fan);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint DFanGet(IntPtr fan, int units, out int speed);

        static bool tried, ok; static IntPtr api;
        public static string Error = "";
        public static DProps GetProps; public static DDev Waiver, ResetDef;
        public static DSetD SetGpuF, SetVramF, SetPow, SetTemp; public static DGetD GetGpuF, GetVramF, GetPow, GetTemp;
        public static DFans EnumFans; public static DFanSet FanFixed; public static DFanDef FanDefault; public static DFanGet FanGet;
        static List<IntPtr> devs = new List<IntPtr>();
        static HashSet<IntPtr> usedDev = new HashSet<IntPtr>();

        static T Fn<T>(IntPtr lib, string name) where T : class
        {
            IntPtr p = GetProcAddress(lib, name);
            return p == IntPtr.Zero ? null : (Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T);
        }

        static IntPtr FindLib()
        {
            IntPtr h = LoadLibrary("ControlLib.dll");
            if (h != IntPtr.Zero) return h;
            try
            {
                string repo = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", @"System32\DriverStore\FileRepository");
                if (Directory.Exists(repo))
                    foreach (string f in Directory.GetFiles(repo, "ControlLib.dll", SearchOption.AllDirectories))
                    {
                        h = LoadLibrary(f);
                        if (h != IntPtr.Zero) return h;
                    }
            }
            catch (Exception) { }
            return IntPtr.Zero;
        }

        static bool Init()
        {
            if (tried) return ok;
            tried = true;
            try
            {
                IntPtr lib = FindLib();
                if (lib == IntPtr.Zero) { Error = "ControlLib.dll não encontrada (instale o driver Intel Arc / Graphics mais recente)"; return false; }
                DInit init = Fn<DInit>(lib, "ctlInit");
                DEnum en = Fn<DEnum>(lib, "ctlEnumerateDevices");
                GetProps = Fn<DProps>(lib, "ctlOverclockGetProperties");
                Waiver = Fn<DDev>(lib, "ctlOverclockWaiverSet");
                ResetDef = Fn<DDev>(lib, "ctlOverclockResetToDefault");
                SetGpuF = Fn<DSetD>(lib, "ctlOverclockGpuFrequencyOffsetSet"); GetGpuF = Fn<DGetD>(lib, "ctlOverclockGpuFrequencyOffsetGet");
                SetVramF = Fn<DSetD>(lib, "ctlOverclockVramFrequencyOffsetSet"); GetVramF = Fn<DGetD>(lib, "ctlOverclockVramFrequencyOffsetGet");
                SetPow = Fn<DSetD>(lib, "ctlOverclockPowerLimitSet"); GetPow = Fn<DGetD>(lib, "ctlOverclockPowerLimitGet");
                SetTemp = Fn<DSetD>(lib, "ctlOverclockTemperatureLimitSet"); GetTemp = Fn<DGetD>(lib, "ctlOverclockTemperatureLimitGet");
                EnumFans = Fn<DFans>(lib, "ctlEnumFans"); FanFixed = Fn<DFanSet>(lib, "ctlFanSetFixedSpeedMode");
                FanDefault = Fn<DFanDef>(lib, "ctlFanSetDefaultMode"); FanGet = Fn<DFanGet>(lib, "ctlFanGetState");
                if (init == null || en == null) { Error = "IGCL incompleta"; return false; }

                IntPtr args = Marshal.AllocHGlobal(64);
                for (int i = 0; i < 64; i += 8) Marshal.WriteInt64(args, i, 0);
                Marshal.WriteInt32(args, 0, 36);          // Size
                Marshal.WriteByte(args, 4, 0);            // Version
                Marshal.WriteInt32(args, 8, 0x00010000);  // AppVersion 1.0
                uint r = init(args, out api);
                Marshal.FreeHGlobal(args);
                if (r != 0 || api == IntPtr.Zero) { Error = "ctlInit erro 0x" + r.ToString("X"); return false; }
                uint n = 0;
                r = en(api, ref n, IntPtr.Zero);
                if (r != 0 || n == 0 || n > 16) { Error = "nenhum dispositivo Intel (ctlEnumerateDevices 0x" + r.ToString("X") + ")"; return false; }
                IntPtr arr = Marshal.AllocHGlobal((int)n * IntPtr.Size);
                try
                {
                    for (int i = 0; i < (int)n * IntPtr.Size; i++) Marshal.WriteByte(arr, i, 0);
                    r = en(api, ref n, arr);
                    if (r != 0) { Error = "ctlEnumerateDevices 0x" + r.ToString("X"); return false; }
                    for (int i = 0; i < (int)n; i++) { IntPtr d = Marshal.ReadIntPtr(arr, i * IntPtr.Size); if (d != IntPtr.Zero) devs.Add(d); }
                }
                finally { Marshal.FreeHGlobal(arr); }
                ok = devs.Count > 0;
            }
            catch (Exception ex) { Error = "IGCL: " + ex.Message; ok = false; }
            return ok;
        }

        // le o bloco ctl_oc_control_info_t (48 bytes) em offset 'o'
        public static bool Info(IntPtr p, int o, out double mn, out double mx, out double df, out int units)
        {
            mn = mx = df = 0; units = 0;
            if (Marshal.ReadByte(p, o) == 0) return false;
            units = Marshal.ReadInt32(p, o + 4);
            mn = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, o + 8));
            mx = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, o + 16));
            df = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, o + 32));
            return !double.IsNaN(mn) && !double.IsNaN(mx) && mx >= mn && Math.Abs(mn) < 1e7 && Math.Abs(mx) < 1e7;
        }

        public static GpuOc Attach(Gpu g, out string why)
        {
            why = "";
            if (!Init()) { why = Error; return null; }
            if (GetProps == null) { why = "IGCL sem Overclock"; return null; }
            foreach (IntPtr d in devs)
            {
                if (usedDev.Contains(d)) continue;
                IntPtr buf = Marshal.AllocHGlobal(2048);
                try
                {
                    int[] sizes = { 296, 248, 344, 200 };
                    foreach (int sz in sizes)
                    {
                        for (int i = 0; i < 2048; i += 8) Marshal.WriteInt64(buf, i, 0);
                        Marshal.WriteInt32(buf, 0, sz); Marshal.WriteByte(buf, 4, 0);
                        if (GetProps(d, buf) == 0 && Marshal.ReadByte(buf, 5) != 0)
                        {
                            usedDev.Add(d);
                            IntelOc oc = new IntelOc(d, sz);
                            oc.Refresh();
                            return oc;
                        }
                    }
                }
                catch (Exception ex) { why = "IGCL: " + ex.Message; }
                finally { Marshal.FreeHGlobal(buf); }
            }
            if (why.Length == 0) why = "o driver Intel não liberou overclock para esta GPU (só Arc dedicadas; gráficos integrados não tem)";
            return null;
        }
    }

    class IntelOc : GpuOc
    {
        IntPtr dev; int propSize; bool waived; IntPtr fan; bool fanTried;
        double pScale = 1.0;
        public IntelOc(IntPtr d, int size) { dev = d; propSize = size; Backend = "Intel (IGCL)"; }

        OcCap FromInfo(IntPtr p, int o, string unit, bool isPower)
        {
            OcCap c = new OcCap(); c.Unit = unit;
            double mn, mx, df; int u;
            if (!Igcl.Info(p, o, out mn, out mx, out df, out u)) return c;
            double sc = 1.0;
            if (isPower && u == 10) sc = 1000.0;   // milliwatts -> W
            if (isPower) pScale = sc;
            c.Ok = true; c.Min = (int)Math.Round(mn / sc); c.Max = (int)Math.Round(mx / sc); c.Def = (int)Math.Round(df / sc); c.Cur = c.Def;
            return c;
        }

        public override void Refresh()
        {
            IntPtr buf = Marshal.AllocHGlobal(2048);
            try
            {
                for (int i = 0; i < 2048; i += 8) Marshal.WriteInt64(buf, i, 0);
                Marshal.WriteInt32(buf, 0, propSize); Marshal.WriteByte(buf, 4, 0);
                uint r = Igcl.GetProps(dev, buf);
                if (r == 0)
                {
                    Core = FromInfo(buf, 8, "MHz", false);
                    Mem = FromInfo(buf, 8 + 96, "MHz", false);
                    Power = FromInfo(buf, 8 + 192, "W", true);
                    Temp = FromInfo(buf, 8 + 240, "\u00B0C", false);
                }
            }
            catch (Exception) { }
            finally { Marshal.FreeHGlobal(buf); }
            double v;
            try
            {
                if (Core.Ok && Igcl.GetGpuF != null && Igcl.GetGpuF(dev, out v) == 0) Core.Cur = (int)Math.Round(v);
                if (Mem.Ok && Igcl.GetVramF != null && Igcl.GetVramF(dev, out v) == 0) Mem.Cur = (int)Math.Round(v);
                if (Power.Ok && Igcl.GetPow != null && Igcl.GetPow(dev, out v) == 0) Power.Cur = (int)Math.Round(v / pScale);
                if (Temp.Ok && Igcl.GetTemp != null && Igcl.GetTemp(dev, out v) == 0) Temp.Cur = (int)Math.Round(v);
            }
            catch (Exception) { }
            string why = "este ajuste não e exposto pelo driver Intel desta GPU";
            if (!Core.Ok) Core.Why = why; if (!Mem.Ok) Mem.Why = why; if (!Power.Ok) Power.Why = why; if (!Temp.Ok) Temp.Why = why;
            Note = "Intel IGCL (experimental): o driver Intel valida os limites; valores fora da faixa são recusados.";
            TryFan();
        }

        void TryFan()
        {
            if (fanTried) return;
            fanTried = true;
            try
            {
                if (Igcl.EnumFans == null || Igcl.FanFixed == null) return;
                uint n = 0;
                if (Igcl.EnumFans(dev, ref n, IntPtr.Zero) != 0 || n == 0 || n > 8) return;
                IntPtr arr = Marshal.AllocHGlobal((int)n * IntPtr.Size);
                try
                {
                    for (int i = 0; i < (int)n * IntPtr.Size; i++) Marshal.WriteByte(arr, i, 0);
                    if (Igcl.EnumFans(dev, ref n, arr) == 0) { fan = Marshal.ReadIntPtr(arr, 0); CanFan = fan != IntPtr.Zero; }
                }
                finally { Marshal.FreeHGlobal(arr); }
            }
            catch (Exception) { }
        }

        string Waiver()
        {
            if (waived) return null;
            try { if (Igcl.Waiver != null) { uint r = Igcl.Waiver(dev); if (r != 0) return "ctlOverclockWaiverSet 0x" + r.ToString("X"); } }
            catch (Exception ex) { return ex.Message; }
            waived = true; return null;
        }
        string Do(Igcl.DSetD f, double v, string nome)
        {
            string w = Waiver();
            if (w != null) return "Falhou: " + w;
            if (f == null) return "Falhou: função ausente";
            try { uint r = f(dev, v); return r == 0 ? "OK (IGCL)" : "Falhou: " + nome + " erro 0x" + r.ToString("X"); }
            catch (Exception ex) { return "Falhou: " + ex.Message; }
        }
        public override string SetCore(int v) { return Core.Ok ? Do(Igcl.SetGpuF, v, "GpuFrequencyOffset") : "não suportado"; }
        public override string SetMem(int v) { return Mem.Ok ? Do(Igcl.SetVramF, v, "VramFrequencyOffset") : "não suportado"; }
        public override string SetPower(int v) { return Power.Ok ? Do(Igcl.SetPow, v * pScale, "PowerLimit") : "não suportado"; }
        public override string SetTemp(int v) { return Temp.Ok ? Do(Igcl.SetTemp, v, "TemperatureLimit") : "não suportado"; }
        public override string ResetAll()
        {
            try { if (Igcl.ResetDef != null && Igcl.ResetDef(dev) == 0) return "OK"; } catch (Exception) { }
            string a = SetCore(0), b = SetMem(0);
            return GpuOc.IsOk(a) || GpuOc.IsOk(b) ? "OK" : "Falhou";
        }
        public override string SetFan(int pct)
        {
            TryFan();
            if (fan == IntPtr.Zero) return "A placa Intel não expõe controle de ventoinha.";
            IntPtr p = Marshal.AllocHGlobal(32);
            try
            {
                for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(p, i, 0);
                Marshal.WriteInt32(p, 0, 16); Marshal.WriteInt32(p, 8, pct); Marshal.WriteInt32(p, 12, 1); // 1 = percentual
                uint r = Igcl.FanFixed(fan, p);
                return r == 0 ? "OK (IGCL): ventoinha em " + pct + "%" : "Falhou: ctlFanSetFixedSpeedMode erro 0x" + r.ToString("X");
            }
            catch (Exception ex) { return "Falhou: " + ex.Message; }
            finally { Marshal.FreeHGlobal(p); }
        }
        public override string FanAuto()
        {
            if (fan == IntPtr.Zero || Igcl.FanDefault == null) return "Falhou";
            try { return Igcl.FanDefault(fan) == 0 ? "OK" : "Falhou"; } catch (Exception) { return "Falhou"; }
        }
        public override int? FanPct()
        {
            if (fan == IntPtr.Zero || Igcl.FanGet == null) return null;
            try { int s; if (Igcl.FanGet(fan, 1, out s) == 0) return s; } catch (Exception) { }
            return null;
        }
        public override int? FanRpm()
        {
            if (fan == IntPtr.Zero || Igcl.FanGet == null) return null;
            try { int s; if (Igcl.FanGet(fan, 0, out s) == 0) return s; } catch (Exception) { }
            return null;
        }
    }

    // VRAM e versao do driver direto do registro (WMI limita em 4 GB)
    static class GpuReg
    {
        public static void Read(string name, out long vram, out string driver, out string driverDate)
        {
            vram = 0; driver = null; driverDate = null;
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return;
                    foreach (string sk in cls.GetSubKeyNames())
                    {
                        if (sk.Length != 4) continue;
                        using (RegistryKey k = cls.OpenSubKey(sk))
                        {
                            if (k == null) continue;
                            string desc = Convert.ToString(k.GetValue("DriverDesc"));
                            if (string.IsNullOrEmpty(desc) || !(desc.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(desc, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                            object q = k.GetValue("HardwareInformation.qwMemorySize");
                            if (q != null) { try { vram = Convert.ToInt64(q); } catch (Exception) { } }
                            if (vram == 0)
                            {
                                byte[] b = k.GetValue("HardwareInformation.MemorySize") as byte[];
                                if (b != null && b.Length >= 4) vram = BitConverter.ToUInt32(b, 0);
                            }
                            driver = Convert.ToString(k.GetValue("DriverVersion"));
                            driverDate = Convert.ToString(k.GetValue("DriverDate"));
                            return;
                        }
                    }
                }
            }
            catch (Exception) { }
        }
    }

    class Gpu
    {
        public string Name, Vendor;
        public LHM.IHardware Hw;
        public IntPtr H;
        public bool Nv;
        public int NvIndex;
        public GpuOc Oc;          // backend de OC/ventoinha (NVIDIA, AMD ou Intel)
        public string OcWhy = ""; // por que nao ha backend

        public void AttachOc()
        {
            try
            {
                if (Nv) { Oc = new NvOc(this); return; }
                string why;
                if (Settings.Get("vendoroc", "1") == "0") { OcWhy = "desligado no .ini (vendoroc=0)"; return; }
                if (Vendor == "AMD") Oc = Adl.Attach(this, out why);
                else if (Vendor == "Intel") Oc = Igcl.Attach(this, out why);
                else { why = "fabricante sem suporte de OC nesta versão"; }
                OcWhy = why ?? "";
            }
            catch (Exception ex) { OcWhy = ex.Message; Oc = null; }
        }
        // O driver NVIDIA expressa o offset da memoria em taxa efetiva (2x o clock mostrado no monitor).
        // O programa converte: o usuario digita MHz reais e o driver recebe o dobro. (Mude para 1 se quiser o valor cru.)
        public const int MemFactor = 2;

        public bool CanFan
        {
            get
            {
                if (Nv) return true;
                if (Hw != null) foreach (LHM.ISensor x in Hw.Sensors) if (x.SensorType.ToString() == "Control" && x.Control != null) return true;
                return Oc != null && Oc.CanFan;
            }
        }
        public override string ToString() { return Name + (Vendor == "NVIDIA" ? "" : "  [" + Vendor + "]"); }

        public float? Lhm(string type, params string[] names)
        {
            if (Hw == null) return null;
            foreach (LHM.ISensor s in Hw.Sensors)
            {
                if (s.SensorType.ToString() != type || !s.Value.HasValue) continue;
                if (names.Length == 0) return s.Value;
                foreach (string n in names)
                    if (s.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return s.Value;
            }
            return null;
        }
        static float? F(uint? v) { return v.HasValue ? (float?)v.Value : null; }

        public int? Temp()
        {
            if (Nv) { uint? t = Nvml.Temp(H); if (t.HasValue) return (int)t.Value; }
            float? f = Lhm("Temperature", "Core");
            if (!f.HasValue) f = Lhm("Temperature");
            return f.HasValue ? (int?)Math.Round(f.Value) : null;
        }
        public int? FanPct()
        {
            if (Nv) { uint? v = Nvml.Fan(H, 0); if (v.HasValue) return (int)v.Value; }
            float? f = Lhm("Control", "Fan");
            if (f.HasValue) return (int?)Math.Round(f.Value);
            return Oc != null ? Oc.FanPct() : null;
        }
        public int? FanRpm()
        {
            if (Nv) { uint? v = Nvml.FanRpm(H, 0); if (v.HasValue) return (int)v.Value; }
            float? f = Lhm("Fan");
            if (f.HasValue) return (int?)Math.Round(f.Value);
            return Oc != null ? Oc.FanRpm() : null;
        }
        public int FanCount()
        {
            if (Nv) { uint? n = Nvml.NumFans(H); if (n.HasValue && n.Value > 0) return (int)n.Value; }
            int c = 0;
            if (Hw != null) foreach (LHM.ISensor s in Hw.Sensors) if (s.SensorType.ToString() == "Fan") c++;
            return c;
        }
        public float? PowerW()
        {
            if (Nv) { uint? p = Nvml.Power(H); if (p.HasValue) return p.Value / 1000f; }
            return Lhm("Power");
        }
        public float? Clk(int type, string lhmName)
        {
            if (Nv) { uint? c = Nvml.Clock(H, type); if (c.HasValue) return c.Value; }
            return Lhm("Clock", lhmName);
        }

        public bool SetFan(int pct, out string msg)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            string nvErr = null;
            if (Nv)
            {
                int n = Math.Max(1, FanCount());
                bool all = true; int last = 0;
                for (int f = 0; f < n; f++)
                {
                    Nvml.SetFanPolicy(H, (uint)f, 1); // 1 = manual
                    int r = Nvml.SetFan(H, (uint)f, (uint)pct);
                    if (r != 0) { all = false; last = r; }
                }
                if (all) { msg = "OK (NVML): ventoinha(s) em " + pct + "%"; return true; }
                nvErr = Nvml.Err(last);
            }
            int done = 0;
            if (Hw != null)
                foreach (LHM.ISensor s in Hw.Sensors)
                    if (s.SensorType.ToString() == "Control" && s.Control != null)
                    {
                        try { s.Control.SetSoftware(pct); done++; } catch (Exception) { }
                    }
            if (done > 0) { msg = "OK (LibreHardwareMonitor): " + done + " canal(is) em " + pct + "%"; return true; }
            if (Oc != null && Oc.CanFan)
            {
                string r = Oc.SetFan(pct);
                if (GpuOc.IsOk(r)) { msg = r; return true; }
                nvErr = (nvErr != null ? nvErr + " | " : "") + r;
            }
            msg = "Falhou" + (nvErr != null ? " - " + nvErr : "") + ". Esta placa/driver não aceitou controle manual.";
            return false;
        }

        // Offset de clock (MHz). Tenta NVML antigo -> NVML novo -> NvAPI. Retorna resumo.
        public string SetClockOffset(bool core, int mhz)
        {
            List<string> errs = new List<string>();
            int drv = core ? mhz : mhz * MemFactor;
            int r = core ? Nvml.SetGpcOffset(H, drv) : Nvml.SetMemOffset(H, drv);
            if (r == 0) return "OK (NVML)";
            errs.Add("NVML: " + Nvml.Err(r));
            r = Nvml.SetOffsetNew(H, core ? 0 : 2, drv);
            if (r == 0) return "OK (NVML novo)";
            errs.Add("NVML2: " + Nvml.Err(r));
            string e = NvApi.SetOffset(NvIndex, core ? 0 : 4, drv * 1000);
            if (e == null) return "OK (NvAPI)";
            errs.Add(e);
            return "FALHOU - " + string.Join(" | ", errs.ToArray());
        }

        public bool SetFanAuto(out string msg)
        {
            bool ok = false;
            if (Nv)
            {
                int n = Math.Max(1, FanCount());
                ok = true;
                for (int f = 0; f < n; f++)
                {
                    int r1 = Nvml.SetFanPolicy(H, (uint)f, 0);
                    int r2 = Nvml.SetFanDefault(H, (uint)f);
                    if (r1 != 0 && r2 != 0) ok = false;
                }
            }
            if (!ok && Hw != null)
                foreach (LHM.ISensor s in Hw.Sensors)
                    if (s.SensorType.ToString() == "Control" && s.Control != null)
                    {
                        try { s.Control.SetDefault(); ok = true; } catch (Exception) { }
                    }
            if (!ok && Oc != null && Oc.CanFan) ok = GpuOc.IsOk(Oc.FanAuto());
            msg = ok ? "Controle automático (curva original da placa) restaurado." : "Não foi possível restaurar o modo automático.";
            return ok;
        }

        // Sensores extras lidos direto do NVML (somam com os do LibreHardwareMonitor)
        static Sn Mk(string g, string n, string t, Func<float?> f) { Sn s = new Sn(); s.Group = g; s.Name = n; s.Type = t; s.Get = f; return s; }
        public List<Sn> Virtual()
        {
            List<Sn> l = new List<Sn>();
            if (!Nv) return l;
            IntPtr h = H;
            string g = "NVML - " + Name;
            l.Add(Mk(g, "Temperatura do núcleo", "Temperature", delegate { return F(Nvml.Temp(h)); }));
            l.Add(Mk(g, "Uso do núcleo", "Load", delegate { uint a, b; return Nvml.GetUtil(h, out a, out b) ? (float?)a : null; }));
            l.Add(Mk(g, "Uso do controlador de memória", "Load", delegate { uint a, b; return Nvml.GetUtil(h, out a, out b) ? (float?)b : null; }));
            l.Add(Mk(g, "VRAM usada", "SmallData", delegate { ulong t, u; return Nvml.Mem(h, out t, out u) ? (float?)(u / 1048576.0) : null; }));
            l.Add(Mk(g, "VRAM total", "SmallData", delegate { ulong t, u; return Nvml.Mem(h, out t, out u) ? (float?)(t / 1048576.0) : null; }));
            l.Add(Mk(g, "VRAM em uso (%)", "Load", delegate { ulong t, u; return Nvml.Mem(h, out t, out u) && t > 0 ? (float?)(100.0 * u / t) : null; }));
            l.Add(Mk(g, "Consumo (watts)", "Power", delegate { uint? p = Nvml.Power(h); return p.HasValue ? (float?)(p.Value / 1000f) : null; }));
            l.Add(Mk(g, "Limite de potência (TDP)", "Power", delegate { uint? p = Nvml.PowerLimit(h); return p.HasValue ? (float?)(p.Value / 1000f) : null; }));
            l.Add(Mk(g, "Ventoinha (%)", "Control", delegate { return F(Nvml.Fan(h, 0)); }));
            l.Add(Mk(g, "Ventoinha (RPM)", "Fan", delegate { return F(Nvml.FanRpm(h, 0)); }));
            l.Add(Mk(g, "Clock do núcleo", "Clock", delegate { return F(Nvml.Clock(h, 0)); }));
            l.Add(Mk(g, "Clock SM", "Clock", delegate { return F(Nvml.Clock(h, 1)); }));
            l.Add(Mk(g, "Clock da memória", "Clock", delegate { return F(Nvml.Clock(h, 2)); }));
            l.Add(Mk(g, "Clock de video", "Clock", delegate { return F(Nvml.Clock(h, 3)); }));
            l.Add(Mk(g, "PCIe - geração", "Factor", delegate { return F(Nvml.Pcie(h, false, false)); }));
            l.Add(Mk(g, "PCIe - largura (x)", "Factor", delegate { return F(Nvml.Pcie(h, true, false)); }));
            return l;
        }

        static string Fm(uint? v, string suf) { return v.HasValue ? v.Value + suf : null; }

        public List<string[]> Specs()
        {
            List<string[]> s = new List<string[]>();
            Action<string, string> add = delegate(string k, string v) { if (v != null) s.Add(new string[] { k, v }); };
            add("Nome", Name);
            add("Fabricante", Vendor);
            if (Nv)
            {
                add("Driver NVIDIA", Nvml.Driver());
                add("Arquitetura", Nvml.Arch(H));
                add("Compute capability (CUDA)", Nvml.Compute(H));
                add("Núcleos CUDA", Fm(Nvml.Cores(H), ""));
                add("VBIOS", Nvml.Vbios(H));
                add("UUID", Nvml.Uuid(H));
                ulong t, u;
                if (Nvml.Mem(H, out t, out u)) add("Memória de video (VRAM)", (t / 1048576UL) + " MB");
                add("Barramento da memória", Fm(Nvml.MemBus(H), " bits"));
                uint? g1 = Nvml.Pcie(H, false, false), w1 = Nvml.Pcie(H, true, false), g2 = Nvml.Pcie(H, false, true), w2 = Nvml.Pcie(H, true, true);
                if (g1.HasValue && w1.HasValue) add("PCIe (agora)", "Gen " + g1.Value + " x" + w1.Value);
                if (g2.HasValue && w2.HasValue) add("PCIe (máximo da placa)", "Gen " + g2.Value + " x" + w2.Value);
                string[] cn = { "Clock do núcleo", "Clock SM", "Clock da memória", "Clock de video" };
                for (int i = 0; i < 4; i++)
                {
                    uint? c = Nvml.Clock(H, i), m = Nvml.MaxClock(H, i);
                    if (c.HasValue || m.HasValue) add(cn[i] + " (agora / máximo)", (c.HasValue ? c.Value.ToString() : "?") + " / " + (m.HasValue ? m.Value.ToString() : "?") + " MHz");
                }
                uint? pl = Nvml.PowerLimit(H), pd = Nvml.PowerDefault(H);
                uint pmn, pmx;
                if (pl.HasValue) add("Limite de potência atual", (pl.Value / 1000f).ToString("0") + " W");
                if (pd.HasValue) add("Limite de potência padrão", (pd.Value / 1000f).ToString("0") + " W");
                if (Nvml.PowerRange(H, out pmn, out pmx)) add("Faixa de potência permitida", (pmn / 1000f).ToString("0") + Lang.Pick(" a ", " to ") + (pmx / 1000f).ToString("0") + " W");
                add("Temp. de desligamento", Fm(Nvml.TempThr(H, 0), " \u00B0C"));
                add("Temp. de slowdown (throttle)", Fm(Nvml.TempThr(H, 1), " \u00B0C"));
                add("Temp. máxima da GPU", Fm(Nvml.TempThr(H, 2), " \u00B0C"));
                add("Temp. máxima da memória", Fm(Nvml.TempThr(H, 3), " \u00B0C"));
                add("Temp. alvo (limite de temperatura)", Fm(Nvml.TempThr(H, 5), " \u00B0C"));
                add("Quantidade de ventoinhas", FanCount().ToString());
                uint fmn, fmx;
                if (Nvml.MinMaxFan(H, out fmn, out fmx)) add("Faixa da ventoinha (driver)", fmn + Lang.Pick("% a ", "% to ") + fmx + "%");
                int? ps = Nvml.Pstate(H);
                if (ps.HasValue) add("Estado de desempenho", "P" + ps.Value);
            }
            else
            {
                add("Leitura de sensores", "LibreHardwareMonitor" + (Oc != null ? " + " + Oc.Backend : ""));
                long vram; string drv, drvDate;
                GpuReg.Read(Name, out vram, out drv, out drvDate);
                if (vram > 0) add("Memória de video (VRAM)", (vram / 1048576L) + " MB");
                add("Driver (registro)", drv);
                add("Data do driver (registro)", drvDate);
                string[] live = { "Temperature|Temperatura", "Temperature|Hot Spot|Temperatura hotspot", "Clock|Core|Clock do núcleo (agora)", "Clock|Memory|Clock da memória (agora)",
                                  "Power|Consumo (agora)", "Load|Core|Uso do núcleo (agora)", "Fan|Ventoinha (RPM agora)", "Control|Ventoinha (% agora)" };
                foreach (string lv in live)
                {
                    string[] p = lv.Split('|');
                    float? val = p.Length == 3 ? Lhm(p[0], p[1]) : Lhm(p[0]);
                    string label = p[p.Length - 1];
                    if (val.HasValue) add(label, val.Value.ToString("0.#") + (p[0] == "Temperature" ? " \u00B0C" : p[0] == "Clock" ? " MHz" : p[0] == "Power" ? " W" : p[0] == "Load" || p[0] == "Control" ? " %" : ""));
                }
                if (Oc != null)
                {
                    add("--- Overclock / controle (" + Oc.Backend + ") ---", " ");
                    Oc.Refresh();
                    Action<string, OcCap> cap = delegate(string k, OcCap c)
                    {
                        if (c.Ok) add(k, "Ligado - faixa " + c.Min + Lang.Pick(" a ", " to ") + c.Max + " " + c.Unit);
                        else add(k, "Indisponível (" + c.Why + ")");
                    };
                    cap("Offset do clock do núcleo", Oc.Core); cap("Offset do clock da memória", Oc.Mem);
                    cap("Limite de potência", Oc.Power); cap("Limite de temperatura", Oc.Temp);
                    add("Controle da ventoinha", Oc.CanFan ? "Ligado" : "Indisponível (placa/driver não expõe)");
                    if (Oc.Note.Length > 0) add("Observação", Oc.Note);
                }
                else add("Overclock / ventoinha", "Indisponível (" + (OcWhy.Length > 0 ? OcWhy : "sem backend para este fabricante") + ")");
            }
            if (Hw != null)
            {
                add("Tipo (LHM)", Hw.HardwareType.ToString());
                float? vm = Lhm("SmallData", "Memory Total");
                if (vm.HasValue && !Nv) add("Memória de video (VRAM)", vm.Value.ToString("0") + " MB");
                add("Sensores expostos (LHM)", Hw.Sensors.Length.ToString());
            }

            add("--- Recursos (ligado / desligado) ---", " ");
            ulong bt, bu;
            if (Nv && Nvml.Bar1(H, out bt, out bu))
            {
                bool big = bt > 268435456UL;
                add("Resizable BAR (Re-Size BAR)", (big ? "Ligado" : "Desligado") + " (BAR1 = " + (bt / 1048576UL) + " MB)");
                add("Above 4G Decoding", big ? "Ligado (exigido pelo BAR grande)" : "Desligado ou não confirmado (BAR1 de 256 MB)");
            }
            else add("Resizable BAR / Above 4G Decoding", Nv ? "Indisponível" : "Indisponível (a leitura exata só existe no driver NVIDIA; veja no AMD Adrenalin: Smart Access Memory, ou na BIOS)");
            add("Boot em modo UEFI (necessário p/ ReBAR)", Platform.UefiText());
            add("Aceleração de GPU por hardware (HAGS)", Platform.Hags());
            add("Modo de jogo (Game Mode)", Platform.GameMode());
            if (Nv)
            {
                int? da = Nvml.DisplayActive(H);
                if (da.HasValue) add("Monitor conectado / display ativo", da.Value == 1 ? "Ligado" : "Desligado");
                int? ecc = Nvml.EccCurrent(H);
                add("ECC da memória", ecc.HasValue ? (ecc.Value == 1 ? "Ligado" : "Desligado") : "Indisponível (placa sem ECC)");
                int? gsp = Nvml.Gsp(H);
                if (gsp.HasValue) add("Firmware GSP", gsp.Value == 1 ? "Ligado" : "Desligado");
                int? cm = Nvml.ComputeMode(H);
                if (cm.HasValue) add("Modo de computação", cm.Value == 0 ? "Padrão" : (cm.Value == 2 ? "Proibido" : "Exclusivo"));
                add("Limitação de clock agora", Nvml.Throttle(H));
            }
            return s;
        }
    }

    // ------------------------------------------------------------------
    //  Informacoes estaticas da CPU (WMI + registro + CPUID do Windows)
    // ------------------------------------------------------------------
    static class CpuInfo
    {
        [DllImport("kernel32.dll")] static extern bool IsProcessorFeaturePresent(uint f);

        public static string P(ManagementBaseObject mo, string name)
        {
            try { object o = mo[name]; return o == null ? null : o.ToString().Trim(); } catch (Exception) { return null; }
        }
        static string Arch(string a)
        {
            switch (a)
            {
                case "0": return "x86"; case "5": return "ARM"; case "6": return "Itanium"; case "9": return "x64"; case "12": return "ARM64";
                default: return a;
            }
        }
        static string Kb(string v) { return (string.IsNullOrEmpty(v) || v == "0") ? null : v + " KB"; }
        static string Mhz(string v) { return string.IsNullOrEmpty(v) ? null : v + " MHz"; }
        static string YesNo(string v) { return v == null ? null : (v.ToLower() == "true" ? "sim" : "não"); }

        public static List<string[]> Collect()
        {
            List<string[]> l = new List<string[]>();
            Action<string, string> add = delegate(string k, string v) { if (!string.IsNullOrEmpty(v)) l.Add(new string[] { k, v }); };
            int coresSum = 0, thrSum = 0;
            try
            {
                using (ManagementObjectSearcher srch = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                using (ManagementObjectCollection col = srch.Get())
                {
                    int n = col.Count, i = 0;
                    foreach (ManagementBaseObject mo in col)
                    {
                        i++;
                        string pre = n > 1 ? "CPU " + i + " - " : "";
                        add(pre + "Nome", P(mo, "Name"));
                        add(pre + "Fabricante", P(mo, "Manufacturer"));
                        add(pre + "Família / modelo / stepping", P(mo, "Description"));
                        add(pre + "Arquitetura", Arch(P(mo, "Architecture")));
                        add(pre + "Soquete", P(mo, "SocketDesignation"));
                        add(pre + "Núcleos físicos", P(mo, "NumberOfCores"));
                        add(pre + "Threads (processadores lógicos)", P(mo, "NumberOfLogicalProcessors"));
                        int cv, tv;
                        if (int.TryParse(P(mo, "NumberOfCores"), out cv)) coresSum += cv;
                        if (int.TryParse(P(mo, "NumberOfLogicalProcessors"), out tv)) thrSum += tv;
                        if (i == 1) CpuId.Socket = P(mo, "SocketDesignation") ?? "";
                        add(pre + "Clock máximo", Mhz(P(mo, "MaxClockSpeed")));
                        add(pre + "Clock atual (WMI)", Mhz(P(mo, "CurrentClockSpeed")));
                        add(pre + "Clock externo (BCLK)", Mhz(P(mo, "ExtClock")));
                        add(pre + "Cache L2", Kb(P(mo, "L2CacheSize")));
                        add(pre + "Cache L3", Kb(P(mo, "L3CacheSize")));
                        string aw = P(mo, "AddressWidth");
                        add(pre + "Largura de endereço", aw == null ? null : aw + " bits");
                        add(pre + "ID do processador", P(mo, "ProcessorId"));
                        add(pre + "Revisão", P(mo, "Revision"));
                        mo.Dispose();
                    }
                }
            }
            catch (Exception ex) { add("Aviso", "WMI indisponível: " + ex.Message); }

            if (l.Count <= 1)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    {
                        if (k != null)
                        {
                            add("Nome", Convert.ToString(k.GetValue("ProcessorNameString")).Trim());
                            add("Identificador", Convert.ToString(k.GetValue("Identifier")));
                            add("Fabricante", Convert.ToString(k.GetValue("VendorIdentifier")));
                            object mhz = k.GetValue("~MHz");
                            if (mhz != null) add("Clock base", mhz + " MHz");
                        }
                    }
                }
                catch (Exception) { }
                add("Threads (processadores lógicos)", Environment.ProcessorCount.ToString());
            }

            string[] fn = { "MMX", "SSE", "SSE2", "SSE3", "SSSE3", "SSE4.1", "SSE4.2", "AVX", "AVX2", "AVX-512" };
            uint[] fid = { 3, 6, 10, 13, 36, 37, 38, 39, 40, 41 };
            StringBuilder sb = new StringBuilder();
            try
            {
                for (int i = 0; i < fn.Length; i++)
                    if (IsProcessorFeaturePresent(fid[i])) { if (sb.Length > 0) sb.Append(", "); sb.Append(fn[i]); }
                add("Conjuntos de instruções", sb.ToString());
            }
            catch (Exception) { }

            CpuId.Cores = coresSum; if (thrSum > 0) CpuId.Threads = thrSum;
            add("--- Recursos e segurança (ligado / desligado) ---", " ");
            if (thrSum > 0 && coresSum > 0)
                add("Hyper-Threading / SMT", thrSum > coresSum ? "Ligado (" + coresSum + " núcleos, " + thrSum + " threads)" : "Desligado (1 thread por núcleo)");
            l.AddRange(Platform.Fast());
            l.AddRange(Platform.Slow());
            return l;
        }
    }

    // ------------------------------------------------------------------
    //  Central de sensores
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    //  Zonas termicas do Windows/ACPI (alternativa quando o driver nao le a CPU)
    // ------------------------------------------------------------------
    static class AcpiTemp
    {
        static Dictionary<string, float> vals = new Dictionary<string, float>();
        static object lk = new object();
        public static void Refresh()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\CIMV2", "SELECT * FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
                using (ManagementObjectCollection col = s.Get())
                    foreach (ManagementBaseObject mo in col)
                    {
                        string n = CpuInfo.P(mo, "Name"), hp = CpuInfo.P(mo, "HighPrecisionTemperature"), t = CpuInfo.P(mo, "Temperature");
                        double k, c = double.NaN;
                        if (hp != null && double.TryParse(hp, NumberStyles.Float, CultureInfo.InvariantCulture, out k) && k > 0) c = k / 10.0 - 273.15;
                        else if (t != null && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out k) && k > 0) c = k - 273.15;
                        if (n != null && !double.IsNaN(c) && c > 0 && c < 150) lock (lk) vals[n] = (float)c;
                        mo.Dispose();
                    }
            }
            catch (Exception) { }
            try
            {
                using (ManagementObjectSearcher s2 = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature"))
                using (ManagementObjectCollection col2 = s2.Get())
                    foreach (ManagementBaseObject mo in col2)
                    {
                        string n = CpuInfo.P(mo, "InstanceName"), t = CpuInfo.P(mo, "CurrentTemperature");
                        double k;
                        if (n != null && t != null && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out k) && k > 0)
                        {
                            double c = k / 10.0 - 273.15;
                            if (c > 0 && c < 150) lock (lk) vals["ACPI " + n] = (float)c;
                        }
                        mo.Dispose();
                    }
            }
            catch (Exception) { }
        }
        public static float? Get(string n) { lock (lk) { float v; return vals.TryGetValue(n, out v) ? (float?)v : null; } }
        public static List<string> Names() { lock (lk) return new List<string>(vals.Keys); }
    }

    static class SensorHub
    {
        static LHM.Computer pc;
        static List<Sn> all = new List<Sn>();
        public static List<Gpu> Gpus = new List<Gpu>();
        public static List<Sn> GpuSensors = new List<Sn>();
        public static List<Sn> PcSensors = new List<Sn>();
        public static List<Sn> AllSensors { get { return all; } }
        public static string Error;

        static List<LHM.IHardware> hwList = new List<LHM.IHardware>();
        static System.Threading.Thread worker;
        static volatile bool running;
        static int tickNo;
        public static volatile string AlertText;
        public static volatile int IntervalMs = 500;
        static DateTime lastSlow = DateTime.MinValue;
        public static void SetInterval(int ms)
        {
            IntervalMs = Math.Max(100, Math.Min(10000, ms));
            Settings.Set("interval", IntervalMs.ToString());
        }
        static void LoadInterval()
        {
            int v;
            if (int.TryParse(Settings.Get("interval", "500"), out v)) IntervalMs = Math.Max(100, Math.Min(10000, v));
        }

        public static Sn Pick(string groupPrefix, string type, params string[] names)
        {
            foreach (string nm in names)
            {
                foreach (Sn s in PcSensors)
                    if (s.Type == type && (type != "Temperature" || s.IsRealTemp) && s.Group.StartsWith(groupPrefix) && s.Seen && string.Equals(s.Name, nm, StringComparison.OrdinalIgnoreCase)) return s;
                foreach (Sn s in PcSensors)
                    if (s.Type == type && (type != "Temperature" || s.IsRealTemp) && s.Group.StartsWith(groupPrefix) && s.Seen && s.Name.IndexOf(nm, StringComparison.OrdinalIgnoreCase) >= 0) return s;
            }
            return null;
        }
        static string GuessVendor(string n)
        {
            string u = n.ToUpper();
            if (u.Contains("NVIDIA") || u.Contains("GEFORCE") || u.Contains("QUADRO")) return "NVIDIA";
            if (u.Contains("AMD") || u.Contains("RADEON") || u.Contains("ATI ")) return "AMD";
            if (u.Contains("INTEL")) return "Intel";
            if (u.Contains("MOORE") || u.Contains("MTT")) return "Moore Threads";
            return "Outro";
        }
        // true se o driver de sensores esta lendo temperatura real da CPU (e nao so a zona termica do Windows)
        public static bool CpuDriverOk()
        {
            foreach (Sn s in PcSensors)
                if (s.Group.StartsWith("Processador") && !s.Group.Contains("Zona térmica") && s.IsRealTemp && s.Seen) return true;
            return false;
        }
        public static float? CpuTemp()
        {
            Sn s = Pick("Processador", "Temperature", "CPU Package", "Package", "Tdie", "CCD", "Core Max", "Core Average", "Tctl", "Core", "");
            return s != null ? s.Last : null;
        }


        static string Kind(string t)
        {
            switch (t)
            {
                case "Cpu": return "Processador";
                case "Motherboard": return "Placa-mae";
                case "SuperIO": return "Placa-mae (Super I/O)";
                case "Memory": return "Memória RAM";
                case "Storage": return "Armazenamento (HD/SSD)";
                case "Cooler": return "Cooler / Water cooler";
                case "Network": return "Rede";
                case "Controller": return "Controladora";
                case "Battery": return "Bateria";
                case "Psu": return "Fonte";
                case "EmbeddedController": return "Controlador embarcado";
                default: return t;
            }
        }

        static bool SameName(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0 || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string FallbackGpuName(Gpu g)
        {
            try
            {
                using (ManagementObjectSearcher vs = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                using (ManagementObjectCollection vc = vs.Get())
                    foreach (ManagementBaseObject vm in vc)
                    {
                        string vn = CpuInfo.P(vm, "Name");
                        vm.Dispose();
                        if (!string.IsNullOrEmpty(vn) && GuessVendor(vn) == g.Vendor) return vn;
                    }
            }
            catch (Exception) { }
            return g.Vendor + " GPU";
        }

        static void Collect(LHM.IHardware h, string group, List<Sn> into)
        {
            foreach (LHM.ISensor s in h.Sensors)
            {
                LHM.ISensor sc = s;
                Sn n = new Sn();
                n.Group = group; n.Name = s.Name; n.Type = s.SensorType.ToString(); n.Ls = s;
                n.Get = delegate { return sc.Value; };
                into.Add(n);
            }
            foreach (LHM.IHardware sub in h.SubHardware) Collect(sub, group + " > " + sub.Name, into);
        }

        public static void Open()
        {
            LoadInterval();
            try
            {
                pc = new LHM.Computer();
                // liga cada tipo de hardware por reflexao: funciona mesmo que sua versao do LibreHardwareMonitor nao tenha alguma propriedade
                foreach (string pn in new string[] { "IsCpuEnabled", "IsGpuEnabled", "IsMemoryEnabled", "IsMotherboardEnabled", "IsControllerEnabled" })
                {
                    try { PropertyInfo pi = pc.GetType().GetProperty(pn); if (pi != null && pi.CanWrite) pi.SetValue(pc, true, null); } catch (Exception) { }
                }
                Startup.Mark("LHM configurado");
                pc.Open();
                Startup.Mark("LHM aberto (CPU, GPU, placa-mae, memória)");
                hwList = new List<LHM.IHardware>(pc.Hardware);
            }
            catch (Exception ex) { Error = ex.Message; pc = null; }

            Nvml.Init();
            Startup.Mark("NVML iniciado");
            List<IntPtr> devs = Nvml.Devices();
            bool[] used = new bool[devs.Count];

            if (pc != null)
            {
                foreach (LHM.IHardware h in pc.Hardware)
                {
                    string t = h.HardwareType.ToString();
                    if (t.StartsWith("Gpu"))
                    {
                        Gpu g = new Gpu();
                        g.Hw = h; g.Name = h.Name;
                        g.Vendor = t == "GpuNvidia" ? "NVIDIA" : t == "GpuAmd" ? "AMD" : t == "GpuIntel" ? "Intel" : t;
                        if (t == "GpuNvidia")
                        {
                            for (int i = 0; i < devs.Count && !g.Nv; i++)
                                if (!used[i] && SameName(Nvml.Name(devs[i]), h.Name)) { used[i] = true; g.H = devs[i]; g.Nv = true; g.NvIndex = i; }
                            for (int i = 0; i < devs.Count && !g.Nv; i++)
                                if (!used[i]) { used[i] = true; g.H = devs[i]; g.Nv = true; g.NvIndex = i; }
                        }
                        Gpus.Add(g);
                        Collect(h, g.Name, GpuSensors);
                    }
                    else Collect(h, Kind(t) + " - " + h.Name, PcSensors);
                }
            }
            for (int i = 0; i < devs.Count; i++)
                if (!used[i])
                {
                    Gpu g = new Gpu();
                    g.Nv = true; g.H = devs[i]; g.NvIndex = i; g.Vendor = "NVIDIA"; g.Name = Nvml.Name(devs[i]) ?? "NVIDIA GPU";
                    Gpus.Add(g);
                }
            if (Gpus.Count == 0)
            {
                // nenhuma GPU achada pelo LibreHardwareMonitor/NVML (placas antigas, marcas menos comuns...): usa o WMI do Windows
                try
                {
                    using (ManagementObjectSearcher vs = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                    using (ManagementObjectCollection vc = vs.Get())
                        foreach (ManagementBaseObject vm in vc)
                        {
                            string vn = CpuInfo.P(vm, "Name");
                            if (!string.IsNullOrEmpty(vn) && vn.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) < 0 && vn.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                Gpu ng = new Gpu(); ng.Name = vn; ng.Vendor = GuessVendor(vn); Gpus.Add(ng);
                            }
                            vm.Dispose();
                        }
                }
                catch (Exception) { }
            }
            foreach (Gpu g in Gpus) if (string.IsNullOrEmpty(g.Name) || g.Name.Trim().Length == 0) g.Name = FallbackGpuName(g);
            Startup.Mark("GPUs listadas");
            foreach (Gpu g in Gpus) g.AttachOc();
            Startup.Mark("OC das GPUs (NVML/ADL/IGCL)");
            foreach (Gpu g in Gpus) GpuSensors.AddRange(g.Virtual());

            foreach (Sn gs in GpuSensors) gs.IsGpu = true;
            all.AddRange(GpuSensors);
            all.AddRange(PcSensors);
            LoadAlerts();
            running = true;
            worker = new System.Threading.Thread(Loop);
            worker.IsBackground = true;
            worker.Name = "BunoSensors";
            worker.Start();
            Startup.Mark("sensores prontos (fase 1)");
            Th.Thread p2 = new Th.Thread(Phase2);
            p2.IsBackground = true; p2.Name = "BunoSensors2"; p2.Start();
        }

        // Fase 2 (em segundo plano): discos (SMART), rede, bateria, fonte e zonas ACPI -> a janela abre sem esperar por eles
        static void Phase2()
        {
            try
            {
                foreach (string pn in new string[] { "IsStorageEnabled", "IsNetworkEnabled", "IsBatteryEnabled", "IsPsuEnabled" })
                {
                    try { PropertyInfo pi = pc.GetType().GetProperty(pn); if (pi != null && pi.CanWrite) pi.SetValue(pc, true, null); } catch (Exception) { }
                    Startup.Mark("fase 2: " + pn);
                }
                List<Sn> add = new List<Sn>();
                List<LHM.IHardware> nh = new List<LHM.IHardware>(hwList);
                foreach (LHM.IHardware h in pc.Hardware)
                {
                    if (hwList.Contains(h)) continue;
                    nh.Add(h);
                    Collect(h, Kind(h.HardwareType.ToString()) + " - " + h.Name, add);
                }
                AcpiTemp.Refresh();
                foreach (string zn in AcpiTemp.Names())
                {
                    Sn z = new Sn(); string nn = zn;
                    z.Group = "Processador - Zona térmica (Windows/ACPI)"; z.Name = "Zona " + zn; z.Type = "Temperature";
                    z.Get = delegate { return AcpiTemp.Get(nn); };
                    add.Add(z);
                }
                // copia e troca: quem esta lendo as listas nunca ve uma lista sendo alterada
                List<Sn> np = new List<Sn>(PcSensors); np.AddRange(add);
                List<Sn> na = new List<Sn>(all); na.AddRange(add);
                foreach (Sn x in add) LoadAlertFor(x);
                hwList = nh; all = na; PcSensors = np;
                Startup.Mark("fase 2 concluída (+" + add.Count + " sensores)");
            }
            catch (Exception ex) { Startup.Mark("fase 2 erro: " + ex.Message); }
        }

        // Toda a leitura pesada (LibreHardwareMonitor) roda aqui, fora da tela -> sem travadas
        static void Loop()
        {
            while (running)
            {
                DateTime t0 = DateTime.UtcNow;
                try { UpdateOnce(); } catch (Exception) { }
                tickNo++;
                int wait = IntervalMs - (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                System.Threading.Thread.Sleep(wait < 20 ? 20 : wait);
            }
        }
        static void UpdateRec(LHM.IHardware h)
        {
            h.Update();
            foreach (LHM.IHardware sub in h.SubHardware) UpdateRec(sub);
        }
        static void UpdateOnce()
        {
            bool slow = (DateTime.UtcNow - lastSlow).TotalSeconds >= 5;   // discos (SMART) e zonas ACPI so a cada 5 s
            if (slow) { lastSlow = DateTime.UtcNow; AcpiTemp.Refresh(); }
            foreach (LHM.IHardware h in hwList)
            {
                if (!running) return;
                string t = h.HardwareType.ToString();
                if (!slow && (t == "Storage" || t == "Network")) continue;
                try { UpdateRec(h); } catch (Exception) { }
            }
            foreach (Sn s in all) s.Poll();

            // alertas (tocam som a cada 15 s enquanto o limite estiver estourado)
            DateTime now = DateTime.UtcNow;
            string txt = null;
            foreach (Sn s in all)
            {
                if (!s.AlertAbove.HasValue || !s.Last.HasValue || s.Last.Value <= s.AlertAbove.Value) continue;
                txt = "ALERTA: " + s.Name + " = " + s.Fmt(s.Last) + " (limite " + s.AlertAbove.Value.ToString("0.##") + ")";
                if ((now - s.LastBeep).TotalSeconds > 15)
                {
                    s.LastBeep = now;
                    try { System.Media.SystemSounds.Exclamation.Play(); } catch (Exception) { }
                }
            }
            AlertText = txt;
            OverheatBeep(now);
        }

        // aviso sonoro de superaquecimento: bipe de 1 s quando QUALQUER sensor de temperatura chega a 95 C.
        // Para tocar de novo e preciso o sensor ter baixado de 95 C e subido outra vez, e ter passado ao menos 1 minuto do ultimo bipe.
        const float OverheatC = 95f;
        static readonly HashSet<string> overHot = new HashSet<string>();
        static DateTime lastOverheat = DateTime.MinValue;
        static void OverheatBeep(DateTime now)
        {
            bool fresh = false;
            foreach (Sn s in all)
            {
                if (s.Type != "Temperature" || !s.IsRealTemp || !s.Seen || !s.Last.HasValue) continue;
                float v = s.Last.Value; string k = s.Key;
                if (v >= OverheatC && v < 200f) { if (overHot.Add(k)) fresh = true; }
                else if (v < OverheatC) overHot.Remove(k);
            }
            if (!fresh || (now - lastOverheat).TotalSeconds < 60) return;
            lastOverheat = now;
            Th.ThreadPool.QueueUserWorkItem(delegate
            {
                try { Console.Beep(1000, 1000); }
                catch (Exception) { try { System.Media.SystemSounds.Hand.Play(); } catch (Exception) { } }
            });
        }

        static void LoadAlerts()
        {
            string v = Settings.Get("alerts", "");
            if (v.Length == 0) return;
            foreach (string e in v.Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = e.Split(new string[] { "~=~" }, StringSplitOptions.None);
                float f;
                if (p.Length != 2 || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out f)) continue;
                foreach (Sn s in all) if (s.Key == p[0]) { s.AlertAbove = f; break; }
            }
        }

        static void LoadAlertFor(Sn x)
        {
            string v = Settings.Get("alerts", "");
            if (v.Length == 0) return;
            foreach (string e in v.Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = e.Split(new string[] { "~=~" }, StringSplitOptions.None);
                float f;
                if (p.Length == 2 && p[0] == x.Key && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out f)) { x.AlertAbove = f; return; }
            }
        }

        public static void SetAlert(Sn s, float? v)
        {
            s.AlertAbove = v;
            List<string> l = new List<string>();
            foreach (Sn x in all)
                if (x.AlertAbove.HasValue) l.Add(x.Key + "~=~" + x.AlertAbove.Value.ToString("0.####", CultureInfo.InvariantCulture));
            Settings.Set("alerts", string.Join("~|~", l.ToArray()));
        }

        public static string Status()
        {
            bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            return "Administrador: " + (admin ? "sim" : "NÃO (rode como administrador)") +
                   "   |   NVML: " + (Nvml.Available ? "ok" : "indisponível") +
                   "   |   LibreHardwareMonitor: " + (pc != null ? "ok" : "erro - " + Error);
        }

        public static void Close()
        {
            running = false;
            try { if (worker != null) worker.Join(2500); } catch (Exception) { }
            Nvml.Shutdown();
            if (pc != null) { try { pc.Close(); } catch (Exception) { } }
        }
    }

    // ------------------------------------------------------------------
    //  Iniciar com o Windows: tarefa agendada no logon com privilegios maximos (o programa exige administrador,
    //  entao a chave Run nao serve: o Windows bloquearia a abertura sem pedir permissao)
    // ------------------------------------------------------------------
    static class AutoStart
    {
        const string TaskName = "BunoTunner";
        public static bool IsOn()
        {
            for (int i = 0; i < 3; i++)
            {
                int c; string o = Proc.Run("schtasks.exe", "/query /tn " + TaskName, 20000, out c);
                if (c == 0) return true;
                if (i < 2) System.Threading.Thread.Sleep(1200);   // logo apos o boot o Agendador pode demorar a responder
            }
            return false;
        }
        public static bool Set(bool on, out string err)
        {
            err = "";
            int c; string o;
            if (!on)
            {
                o = Proc.Run("schtasks.exe", "/delete /tn " + TaskName + " /f", 20000, out c);
                if (c != 0) err = o;
                return c == 0;
            }
            string exe = Application.ExecutablePath, user = WindowsIdentity.GetCurrent().Name;
            Func<string, string> x = delegate(string s) { return System.Security.SecurityElement.Escape(s); };
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "<RegistrationInfo><Description>BunoTunner - inicia com o Windows</Description></RegistrationInfo>\r\n" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + x(user) + "</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>\r\n" +
                "<Principals><Principal id=\"Author\"><UserId>" + x(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                "<AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden>" +
                "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>\r\n" +
                "<Actions Context=\"Author\"><Exec><Command>" + x(exe) + "</Command><Arguments>--tray</Arguments><WorkingDirectory>" + x(Path.GetDirectoryName(exe)) + "</WorkingDirectory></Exec></Actions>\r\n</Task>";
            string tmp = Path.Combine(Path.GetTempPath(), "BunoTunner_task.xml");
            try
            {
                File.WriteAllText(tmp, xml, Encoding.Unicode);
                o = Proc.Run("schtasks.exe", "/create /tn " + TaskName + " /xml \"" + tmp + "\" /f", 30000, out c);
                if (c != 0) err = o;
                return c == 0;
            }
            catch (Exception ex) { err = ex.Message; return false; }
            finally { try { File.Delete(tmp); } catch (Exception) { } }
        }
    }

    // ------------------------------------------------------------------
    //  Configuracoes salvas ao lado do .exe (BunoTunner.ini) - portable
    // ------------------------------------------------------------------
    static class Settings
    {
        static Dictionary<string, string> d;
        static string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BunoTunner.ini");
        static void Load()
        {
            d = new Dictionary<string, string>();
            try
            {
                string old = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BunosTunner.ini");
                if (!File.Exists(path) && File.Exists(old)) File.Copy(old, path);   // migra as configuracoes do nome antigo
                if (File.Exists(path))
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                    }
            }
            catch (Exception) { }
        }
        public static string Get(string k, string def)
        {
            if (d == null) Load();
            string v;
            return d.TryGetValue(k, out v) ? v : def;
        }
        public static void Set(string k, string v)
        {
            if (d == null) Load();
            d[k] = v;
            try
            {
                List<string> l = new List<string>();
                foreach (KeyValuePair<string, string> p in d) l.Add(p.Key + "=" + p.Value);
                File.WriteAllLines(path, l.ToArray(), Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }

    // ------------------------------------------------------------------
    //  Caixa de dialogo simples (pede um texto/valor)
    // ------------------------------------------------------------------
    class PromptForm : XpForm
    {
        XpText tx = new XpText();
        public string Result;
        public PromptForm(string title, string msg, string def)
        {
            Text = title; Size = new Size(400, 170); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            Label l = new Label(); l.BackColor = Color.Transparent; l.Font = Xp.Ui; l.SetBounds(14, 40, 372, 40); l.Text = msg;
            Controls.Add(l);
            tx.SetBounds(14, 86, 372, tx.Height); tx.Text = def; Controls.Add(tx);
            UI.Btn(this, "OK", 220, 118, 80, delegate { Commit(); });
            UI.Btn(this, "Cancelar", 306, 118, 80, delegate { Close(); });
            tx.Box.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Commit(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
            };
            Shown += delegate { tx.Box.Focus(); tx.Box.SelectAll(); };
        }
        void Commit() { Result = tx.Text.Trim(); DialogResult = DialogResult.OK; Close(); }
        public static string Ask(IWin32Window owner, string title, string msg, string def)
        {
            using (PromptForm f = new PromptForm(title, msg, def))
                return f.ShowDialog(owner) == DialogResult.OK ? f.Result : null;
        }
    }

    // ------------------------------------------------------------------
    //  Janela de sensores (GPU e resto do PC): cartoes, filtros, grafico, alertas, exportar
    // ------------------------------------------------------------------
    class TileDef
    {
        public string Title, Group, Type; public string[] Names;
        public TileDef(string title, string group, string type, params string[] names) { Title = title; Group = group; Type = type; Names = names; }
    }

    class SnSorter : System.Collections.IComparer
    {
        int col; bool asc;
        public SnSorter(int c, bool a) { col = c; asc = a; }
        static float N(float? v) { return v.HasValue ? v.Value : float.MinValue; }
        public int Compare(object x, object y)
        {
            Sn p = (Sn)((ListViewItem)x).Tag, q = (Sn)((ListViewItem)y).Tag;
            int r;
            switch (col)
            {
                case 1: r = N(p.Last).CompareTo(N(q.Last)); break;
                case 2: r = p.Min.CompareTo(q.Min); break;
                case 3: r = p.Max.CompareTo(q.Max); break;
                case 4: r = string.Compare(p.Category, q.Category, StringComparison.OrdinalIgnoreCase); break;
                default: r = string.Compare(p.Name, q.Name, StringComparison.OrdinalIgnoreCase); break;
            }
            return asc ? r : -r;
        }
    }

    // ------------------------------------------------------------------
    //  Overlay em jogos: janela transparente por cima do jogo (tema do app) + FPS/API via ETW (como o PresentMon)
    // ------------------------------------------------------------------
    static class FpsMon
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int StartTraceW(out ulong h, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int ControlTraceW(ulong h, string name, IntPtr props, uint code);
        [DllImport("advapi32.dll", ExactSpelling = true)] static extern int EnableTraceEx2(ulong h, ref Guid provider, uint control, byte level, ulong any, ulong all, uint timeout, IntPtr parms);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern ulong OpenTraceW(IntPtr logfile);
        [DllImport("advapi32.dll", ExactSpelling = true)] static extern int ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll", ExactSpelling = true)] static extern int CloseTrace(ulong h);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint acc, bool inh, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("psapi.dll", ExactSpelling = true)] static extern bool EnumProcessModulesEx(IntPtr h, IntPtr[] mods, int cb, out int need, uint filter);
        [DllImport("psapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int GetModuleBaseNameW(IntPtr h, IntPtr mod, StringBuilder sb, int n);

        delegate void EtCb(IntPtr rec);
        static EtCb cb = OnEvent;
        const string SessName = "BunoTunnerFps";
        static ulong sess, trace;
        static volatile bool running;
        static readonly object lk = new object();
        class Kq { public Queue<long> Q = new Queue<long>(); public long Last, Newest; }
        class PidInfo { public Queue<long> Q = new Queue<long>(); public long Last, Newest, ApiAt; public bool D9; public string Api, ApiMode; public Dictionary<int, Kq> K = new Dictionary<int, Kq>(); }
        static readonly int[] KIds = { 0xB8, 0xA8, 0xA6 };   // DxgKrnl: Present_Info, Flip_Info, Blit_Info (apps que nao usam DXGI: Vulkan/OpenGL)
        static readonly Dictionary<uint, PidInfo> pids = new Dictionary<uint, PidInfo>();
        public static string Error = "";
        public static bool Running { get { return running; } }

        static IntPtr MakeProps()
        {
            int sz = IntPtr.Size == 8 ? 120 : 116, total = 1024;
            IntPtr p = Marshal.AllocHGlobal(total);
            for (int i = 0; i < total; i += 8) Marshal.WriteInt64(p, i, 0);
            Marshal.WriteInt32(p, 0, total);          // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);             // Wnode.ClientContext = 1 (relogio QPC)
            Marshal.WriteInt32(p, 44, 0x20000);       // WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(p, 48, 4);             // BufferSize (KB): buffers pequenos = eventos entregues mais rapido
            Marshal.WriteInt32(p, 64, 0x100);         // LogFileMode = REAL_TIME
            Marshal.WriteInt32(p, 68, 1);             // FlushTimer (s): entrega os eventos a cada 1 s
            Marshal.WriteInt32(p, IntPtr.Size == 8 ? 116 : 112, sz);   // LoggerNameOffset
            byte[] nm = Encoding.Unicode.GetBytes(SessName + "\0");
            Marshal.Copy(nm, 0, (IntPtr)((long)p + sz), nm.Length);
            return p;
        }

        public static bool Start()
        {
            if (running) return true;
            try
            {
                if (IntPtr.Size != 8) { Error = "FPS só funciona na versão de 64 bits."; return false; }
                IntPtr p0 = MakeProps(); ControlTraceW(0, SessName, p0, 1); Marshal.FreeHGlobal(p0);   // sessao velha (se o programa fechou sem parar)
                IntPtr p = MakeProps(); int r = StartTraceW(out sess, SessName, p); Marshal.FreeHGlobal(p);
                if (r != 0) { Error = "ETW erro " + r + (r == 5 ? " (acesso negado: execute como administrador)" : ""); return false; }
                Guid g1 = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");   // Microsoft-Windows-DXGI (D3D10/11/12)
                Guid g2 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");   // Microsoft-Windows-D3D9
                EnableTraceEx2(sess, ref g1, 1, 5, ulong.MaxValue, 0, 0, IntPtr.Zero);
                EnableTraceEx2(sess, ref g2, 1, 5, ulong.MaxValue, 0, 0, IntPtr.Zero);
                // DxgKrnl (Vulkan/OpenGL nao passam pelo DXGI): so alguns eventos, filtrados por ID no proprio kernel
                try
                {
                    Guid g3 = new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D");
                    int fsz = 4 + KIds.Length * 2;
                    IntPtr fp = Marshal.AllocHGlobal(fsz);
                    Marshal.WriteByte(fp, 0, 1); Marshal.WriteByte(fp, 1, 0); Marshal.WriteInt16(fp, 2, (short)KIds.Length);
                    for (int i = 0; i < KIds.Length; i++) Marshal.WriteInt16(fp, 4 + i * 2, (short)KIds[i]);
                    IntPtr fd = Marshal.AllocHGlobal(16);
                    Marshal.WriteInt64(fd, 0, (long)fp); Marshal.WriteInt32(fd, 8, fsz); Marshal.WriteInt32(fd, 12, unchecked((int)0x80000200));   // EVENT_FILTER_TYPE_EVENT_ID
                    IntPtr ep = Marshal.AllocHGlobal(48);
                    for (int i = 0; i < 48; i += 8) Marshal.WriteInt64(ep, i, 0);
                    Marshal.WriteInt32(ep, 0, 2); Marshal.WriteIntPtr(ep, 32, fd); Marshal.WriteInt32(ep, 40, 1);
                    EnableTraceEx2(sess, ref g3, 1, 4, ulong.MaxValue, 0, 0, ep);
                }
                catch (Exception) { }
                IntPtr lf = Marshal.AllocHGlobal(448);
                for (int i = 0; i < 448; i += 8) Marshal.WriteInt64(lf, i, 0);
                Marshal.WriteIntPtr(lf, 8, Marshal.StringToHGlobalUni(SessName));      // LoggerName
                Marshal.WriteInt32(lf, 28, 0x10000100);                                // REAL_TIME | EVENT_RECORD
                Marshal.WriteIntPtr(lf, 424, Marshal.GetFunctionPointerForDelegate(cb)); // EventRecordCallback
                trace = OpenTraceW(lf);
                if (trace == ulong.MaxValue) { Error = "OpenTrace falhou"; Stop(); return false; }
                running = true;
                Th.Thread t = new Th.Thread(delegate() { try { ProcessTrace(new ulong[] { trace }, 1, IntPtr.Zero, IntPtr.Zero); } catch (Exception) { } running = false; });
                t.IsBackground = true; t.Name = "BunoFps"; t.Start();
                Error = "";
                return true;
            }
            catch (Exception ex) { Error = ex.Message; return false; }
        }

        public static void Stop()
        {
            try
            {
                IntPtr p = MakeProps(); ControlTraceW(0, SessName, p, 1); Marshal.FreeHGlobal(p);
                if (trace != 0 && trace != ulong.MaxValue) CloseTrace(trace);
            }
            catch (Exception) { }
            trace = 0; running = false;
        }

        static void OnEvent(IntPtr rec)
        {
            try
            {
                int id = Marshal.ReadInt16(rec, 40) & 0xFFFF;
                int g0 = Marshal.ReadInt32(rec, 24);
                bool dxgi = id == 42 && g0 == unchecked((int)0xCA11C036);
                bool d9 = id == 1 && g0 == 0x783ACA0A;
                bool dk = g0 == unchecked((int)0x802EC45A) && Array.IndexOf(KIds, id) >= 0;
                if (!dxgi && !d9 && !dk) return;
                uint pid = (uint)Marshal.ReadInt32(rec, 12);
                long ts = Marshal.ReadInt64(rec, 16), arrive = Stopwatch.GetTimestamp();
                if (dk)
                {
                    lock (lk)
                    {
                        PidInfo pk; if (!pids.TryGetValue(pid, out pk)) { pk = new PidInfo(); pids[pid] = pk; }
                        Kq kq; if (!pk.K.TryGetValue(id, out kq)) { kq = new Kq(); pk.K[id] = kq; }
                        kq.Q.Enqueue(ts); kq.Newest = ts; kq.Last = arrive;
                        long kc = ts - Stopwatch.Frequency * 2;
                        while (kq.Q.Count > 0 && kq.Q.Peek() < kc) kq.Q.Dequeue();
                    }
                    return;
                }
                lock (lk)
                {
                    PidInfo pi; if (!pids.TryGetValue(pid, out pi)) { pi = new PidInfo(); pids[pid] = pi; }
                    pi.Q.Enqueue(ts); pi.Newest = ts; pi.Last = arrive; if (d9) pi.D9 = true;
                    long cut = ts - Stopwatch.Frequency * 2;
                    while (pi.Q.Count > 0 && pi.Q.Peek() < cut) pi.Q.Dequeue();
                }
            }
            catch (Exception) { }
        }

        // quadros por segundo do processo (se ele esta apresentando quadros agora) e a API grafica
        public static bool Get(uint pid, out int fps, out string api)
        {
            fps = 0; api = null; PidInfo pi; bool dk = false; long now = Stopwatch.GetTimestamp(), hz = Stopwatch.Frequency;
            lock (lk)
            {
                if (pids.Count > 64) { List<uint> old = new List<uint>(); foreach (KeyValuePair<uint, PidInfo> k in pids) if (now - k.Value.Last > hz * 30) old.Add(k.Key); foreach (uint o in old) pids.Remove(o); }
                if (!pids.TryGetValue(pid, out pi)) return false;
                Queue<long> q; long newest;
                if (pi.Last != 0 && now - pi.Last <= hz * 3) { q = pi.Q; newest = pi.Newest; }          // DXGI / D3D9
                else
                {
                    q = null; newest = 0; dk = true;
                    foreach (int id in KIds)
                    {
                        Kq kq; if (pi.K.TryGetValue(id, out kq) && now - kq.Last <= hz * 3) { q = kq.Q; newest = kq.Newest; break; }
                    }
                    if (q == null) return false;
                }
                long cut = newest - hz; int n = 0;
                foreach (long t in q) if (t > cut) n++;
                fps = n;
            }
            string mode = dk ? "k" : "d";
            if (pi.Api == null || pi.ApiMode != mode || now - pi.ApiAt > hz * 10) { pi.Api = DetectApi(pid, pi.D9, dk); pi.ApiMode = mode; pi.ApiAt = now; }
            api = pi.Api;
            return fps > 0;
        }

        static string DetectApi(uint pid, bool d9prov, bool dkOnly)
        {
            string r = d9prov ? "DX9" : "DX11";
            IntPtr h = OpenProcess(0x0410, false, pid);
            if (h == IntPtr.Zero) return r;
            try
            {
                IntPtr[] m = new IntPtr[2048]; int need;
                if (!EnumProcessModulesEx(h, m, m.Length * IntPtr.Size, out need, 3)) return r;
                int n = Math.Min(m.Length, need / IntPtr.Size);
                bool d12 = false, d11 = false, d10 = false, d9 = false, vk = false, gl = false;
                StringBuilder sb = new StringBuilder(260);
                for (int i = 0; i < n; i++)
                {
                    sb.Length = 0;
                    if (GetModuleBaseNameW(h, m[i], sb, 260) <= 0) continue;
                    string s = sb.ToString().ToLowerInvariant();
                    if (s == "d3d12.dll" || s == "d3d12core.dll") d12 = true;
                    else if (s == "d3d11.dll") d11 = true;
                    else if (s == "d3d10.dll" || s == "d3d10_1.dll" || s == "d3d10core.dll") d10 = true;
                    else if (s == "d3d9.dll") d9 = true;
                    else if (s == "vulkan-1.dll") vk = true;
                    else if (s == "opengl32.dll") gl = true;
                }
                if (dkOnly) { if (vk) return "Vulkan"; if (gl) return "OpenGL"; }   // sem eventos do DXGI: nao e DirectX
                if (d9prov) return "DX9";
                if (d12) return "DX12";
                if (d11) return "DX11";
                if (d10) return "DX10";
                if (vk) return "Vulkan";
                if (d9) return "DX9";
                if (gl) return "OpenGL";
            }
            catch (Exception) { }
            finally { CloseHandle(h); }
            return r;
        }
    }

    static class Overlay
    {
        public class Row { public string Key, Label, Value; public bool Hot, HasCol; public Color Col; }
        public static readonly string[][] Specials = {
            new string[] { "@FPS", "FPS (API + quadros/s)" } };
        public static Dictionary<string, Color> Colors = new Dictionary<string, Color>();
        public static Dictionary<string, string> Names = new Dictionary<string, string>();
        public static bool Compact, ColName; public static int HotTemp = 80;
        static readonly string[] Sep = { "~|~" };

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

        public static bool On, Editing, Hidden;
        public static int KeyMods, KeyVk;      // atalho para mostrar/ocultar (mods: 1=Ctrl 2=Shift 4=Alt)
        public static int Scale = 100, Mode, Opac = 85, Layout;   // Layout: 0 padrao, 1 sem fundo nem contorno, 2 so contorno, 3 barra lateral, 4 neon, 5 terminal
        public static double Fx, Fy;
        public static List<string> Items = new List<string>();
        public static event Action StateChanged;
        static OverlayForm win; static Timer tm; static Screen editScreen;
        static DateTime lastTry = DateTime.MinValue, fpsAt = DateTime.MinValue;
        static uint fpsPid; static int fpsVal; static string fpsApi; static bool fpsGame;
        static List<Sn> cacheSrc; static int cacheCount = -1; static Dictionary<string, Sn> cache = new Dictionary<string, Sn>();
        static readonly uint myPid = (uint)Process.GetCurrentProcess().Id;

        static int I(string k, int d) { int v; return int.TryParse(Settings.Get(k, d.ToString()), out v) ? v : d; }
        static double Dd(string k, double d) { double v; return double.TryParse(Settings.Get(k, d.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : d; }
        public static void Load()
        {
            On = Settings.Get("ov.on", "0") == "1";
            Scale = Math.Max(50, Math.Min(200, (int)Math.Round(I("ov.size", 100) / 5.0) * 5)); Mode = Math.Max(0, Math.Min(2, I("ov.mode", 0))); Opac = Math.Max(50, Math.Min(100, (int)Math.Round(I("ov.op", 85) / 5.0) * 5)); Layout = Math.Max(0, Math.Min(24, I("ov.lay", 0)));
            Fx = Math.Max(0, Math.Min(1, Dd("ov.fx", 0))); Fy = Math.Max(0, Math.Min(1, Dd("ov.fy", 0)));
            Items = new List<string>(Settings.Get("ov.items", "@FPS").Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries));
            Items.RemoveAll(delegate(string it) { return it.StartsWith("@") && it != "@FPS"; });   // itens removidos em versoes anteriores (frametime etc.)
            KeyMods = I("ov.keymods", 3); KeyVk = I("ov.keyvk", (int)Keys.O);   // padrao: Ctrl+Shift+O
            Compact = Settings.Get("ov.compact", "0") == "1"; ColName = Settings.Get("ov.colname", "0") == "1"; HotTemp = I("ov.hot", 80);
            Colors.Clear(); Names.Clear();
            foreach (string e in Settings.Get("ov.cols", "").Split(Sep, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = e.IndexOf("~=~"); int rgb;
                if (i > 0 && int.TryParse(e.Substring(i + 3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) Colors[e.Substring(0, i)] = Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            }
            foreach (string e in Settings.Get("ov.names", "").Split(Sep, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = e.IndexOf("~=~"); if (i > 0 && e.Length > i + 3) Names[e.Substring(0, i)] = e.Substring(i + 3);
            }
        }
        public static void SetColor(string key, Color? c) { if (c.HasValue) Colors[key] = c.Value; else Colors.Remove(key); Commit(); }
        public static void SetName(string key, string n) { if (string.IsNullOrEmpty(n)) Names.Remove(key); else Names[key] = n.Replace("~", "").Replace("\r", " ").Replace("\n", " "); Commit(); }
        // todas as chaves da configuracao (usadas para salvar, desfazer/refazer e exportar/importar)
        static readonly string[] CfgKeys = { "ov.size", "ov.mode", "ov.op", "ov.lay", "ov.fx", "ov.fy", "ov.items", "ov.compact", "ov.colname", "ov.hot", "ov.cols", "ov.names", "ov.keymods", "ov.keyvk" };
        static List<KeyValuePair<string, string>> Dump()
        {
            List<KeyValuePair<string, string>> d = new List<KeyValuePair<string, string>>();
            List<string> cl = new List<string>(); foreach (KeyValuePair<string, Color> kv in Colors) cl.Add(kv.Key + "~=~" + (kv.Value.R << 16 | kv.Value.G << 8 | kv.Value.B).ToString("X6"));
            List<string> nl = new List<string>(); foreach (KeyValuePair<string, string> kv in Names) nl.Add(kv.Key + "~=~" + kv.Value);
            d.Add(new KeyValuePair<string, string>("ov.size", Scale.ToString())); d.Add(new KeyValuePair<string, string>("ov.mode", Mode.ToString()));
            d.Add(new KeyValuePair<string, string>("ov.op", Opac.ToString())); d.Add(new KeyValuePair<string, string>("ov.lay", Layout.ToString()));
            d.Add(new KeyValuePair<string, string>("ov.fx", Fx.ToString("0.####", CultureInfo.InvariantCulture))); d.Add(new KeyValuePair<string, string>("ov.fy", Fy.ToString("0.####", CultureInfo.InvariantCulture)));
            d.Add(new KeyValuePair<string, string>("ov.items", string.Join("~|~", Items.ToArray())));
            d.Add(new KeyValuePair<string, string>("ov.compact", Compact ? "1" : "0")); d.Add(new KeyValuePair<string, string>("ov.colname", ColName ? "1" : "0"));
            d.Add(new KeyValuePair<string, string>("ov.hot", HotTemp.ToString()));
            d.Add(new KeyValuePair<string, string>("ov.cols", string.Join("~|~", cl.ToArray()))); d.Add(new KeyValuePair<string, string>("ov.names", string.Join("~|~", nl.ToArray())));
            d.Add(new KeyValuePair<string, string>("ov.keymods", KeyMods.ToString())); d.Add(new KeyValuePair<string, string>("ov.keyvk", KeyVk.ToString()));
            return d;
        }
        public static void Save()
        {
            Settings.Set("ov.on", On ? "1" : "0");
            foreach (KeyValuePair<string, string> kv in Dump()) Settings.Set(kv.Key, kv.Value);
        }
        static string Snap() { StringBuilder sb = new StringBuilder(); foreach (KeyValuePair<string, string> kv in Dump()) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n'); return sb.ToString(); }
        static void ApplyText(string text)
        {
            foreach (string line in text.Replace("\r", "").Split('\n'))
            {
                int i = line.IndexOf('='); if (i <= 0) continue;
                string k = line.Substring(0, i).Trim();
                if (Array.IndexOf(CfgKeys, k) >= 0) Settings.Set(k, line.Substring(i + 1));
            }
            bool on = On; Load(); On = on;
        }
        // desfazer / refazer: cada alteracao confirmada (Commit) guarda o estado anterior
        static string lastSnap; static readonly Stack<string> undo = new Stack<string>(), redo = new Stack<string>();
        public static bool CanUndo { get { return undo.Count > 0; } }
        public static bool CanRedo { get { return redo.Count > 0; } }
        static void Record()
        {
            string n = Snap();
            if (lastSnap == null) { lastSnap = n; return; }
            if (n == lastSnap) return;
            undo.Push(lastSnap); if (undo.Count > 100) { Stack<string> t = new Stack<string>(); foreach (string x in undo) t.Push(x); undo.Clear(); string[] arr = t.ToArray(); for (int i = 1; i < arr.Length; i++) undo.Push(arr[i]); }
            redo.Clear(); lastSnap = n;
        }
        public static void Undo()
        {
            if (undo.Count == 0) return;
            redo.Push(lastSnap ?? Snap()); lastSnap = undo.Pop();
            ApplyText(lastSnap); Save(); Notify(); Tick();
        }
        public static void Redo()
        {
            if (redo.Count == 0) return;
            undo.Push(lastSnap ?? Snap()); lastSnap = redo.Pop();
            ApplyText(lastSnap); Save(); Notify(); Tick();
        }
        public static string Export() { StringBuilder sb = new StringBuilder("# BunoTunner - configuração do overlay\n"); sb.Append(Snap()); return sb.ToString(); }
        public static int Import(string text)
        {
            Record();                                  // garante que o estado atual possa ser desfeito
            int n = 0; foreach (string line in text.Replace("\r", "").Split('\n')) { int i = line.IndexOf('='); if (i > 0 && Array.IndexOf(CfgKeys, line.Substring(0, i).Trim()) >= 0) n++; }
            if (n == 0) return 0;
            ApplyText(text); Commit(); return n;
        }
        public static void ResetAll()
        {
            Scale = 100; Opac = 85; Layout = 0; Mode = 0; Fx = 0; Fy = 0; Items = new List<string>(new string[] { "@FPS" }); Colors.Clear(); Names.Clear();
            Compact = false; ColName = false; HotTemp = 80; Commit();
        }
        public static string KeyName()
        {
            if (KeyVk == 0) return "(nenhum)";
            return ((KeyMods & 1) != 0 ? "Ctrl+" : "") + ((KeyMods & 2) != 0 ? "Shift+" : "") + ((KeyMods & 4) != 0 ? "Alt+" : "") + ((Keys)KeyVk).ToString();
        }
        // Atalho global registrado no Windows (RegisterHotKey): a combinacao fica reservada para o BunoTunner e NAO chega aos outros programas.
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        class HotWnd : NativeWindow
        {
            public HotWnd() { CreateParams cp = new CreateParams(); cp.Parent = (IntPtr)(-3); CreateHandle(cp); }   // janela so de mensagens (HWND_MESSAGE)
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x312 && m.WParam.ToInt32() == 0x4254) { try { ToggleVisible(); } catch (Exception) { } return; }
                base.WndProc(ref m);
            }
        }
        static HotWnd hw; static int regMods = -1, regVk = -1; public static bool KeyOk = true; static bool keyPaused;
        // registra (ou re-registra) o atalho conforme a configuracao atual; chamado a cada mudanca
        public static void ApplyKey()
        {
            try
            {
                int vk = keyPaused ? 0 : KeyVk, mods = keyPaused ? 0 : KeyMods;
                if (hw == null) hw = new HotWnd();
                if (vk == regVk && mods == regMods) return;
                if (regVk > 0) UnregisterHotKey(hw.Handle, 0x4254);
                regVk = vk; regMods = mods; KeyOk = true;
                if (vk == 0) return;
                uint m = 0x4000 | ((mods & 1) != 0 ? 2u : 0u) | ((mods & 2) != 0 ? 4u : 0u) | ((mods & 4) != 0 ? 1u : 0u);   // NOREPEAT | CTRL | SHIFT | ALT
                KeyOk = RegisterHotKey(hw.Handle, 0x4254, m, (uint)vk);
            }
            catch (Exception) { KeyOk = false; }
        }
        // durante a captura de um novo atalho, o antigo e liberado para que a combinacao chegue ate a janela de configuracao
        public static void PauseKey(bool p) { keyPaused = p; ApplyKey(); }
        public static void ToggleVisible()
        {
            if (!On) { On = true; Hidden = false; } else Hidden = !Hidden;
            Save(); Notify(); Tick();
        }
        public static void Init()
        {
            if (tm != null) return;
            Load(); lastSnap = Snap();
            tm = new Timer(); tm.Interval = 500; tm.Tick += delegate { try { Tick(); } catch (Exception) { } };
            tm.Start();
            ApplyKey();
            Application.ApplicationExit += delegate { FpsMon.Stop(); };
        }
        public static void Commit() { Record(); Save(); Notify(); Tick(); }
        public static void Notify() { ApplyKey(); if (StateChanged != null) StateChanged(); }
        public static void SetEditing(bool v, Screen sc)
        {
            Editing = v; if (sc != null) editScreen = sc;
            if (!v) Save();
            Notify(); Tick();
        }
        public static void Corner(double fx, double fy) { Fx = fx; Fy = fy; Commit(); }
        public static void MoveItem(int from, int to)
        {
            if (from < 0 || from >= Items.Count) return;
            string it = Items[from]; Items.RemoveAt(from);
            if (to > from) to--;
            to = Math.Max(0, Math.Min(Items.Count, to));
            Items.Insert(to, it); Commit();
        }
        public static void RemoveAt(int i) { if (i >= 0 && i < Items.Count) { Items.RemoveAt(i); Commit(); } }

        // ---- sensores ----
        public static string OvKey(Sn s) { return s.Type + "~;~" + s.Key; }
        public static Sn Find(string key)
        {
            List<Sn> src = SensorHub.AllSensors;
            if (src != cacheSrc || src.Count != cacheCount)
            {
                cache.Clear(); cacheSrc = src; cacheCount = src.Count;
                foreach (Sn s in src.ToArray()) cache[OvKey(s)] = s;
            }
            Sn r; return cache.TryGetValue(key, out r) ? r : null;
        }
        static string Short(string t)
        {
            switch (t)
            {
                case "Temperature": return "Temp"; case "Load": return "Uso"; case "Clock": return "Clock"; case "Power": return "Pot.";
                case "Fan": return "Fan"; case "Voltage": return "Tensão"; case "Control": return "Ctrl"; default: return "";
            }
        }
        public static string LabelOf(Sn s)
        {
            string n = s.Name, a = s.Area;
            if (a != "Outros" && a != "Coolers" && n.ToUpperInvariant().IndexOf(a.ToUpperInvariant()) < 0) n = a + " " + n;
            string sh = Short(s.Type);
            if (sh.Length > 0 && n.IndexOf(sh, StringComparison.OrdinalIgnoreCase) < 0 && !(sh == "Uso" && n.IndexOf("Load", StringComparison.OrdinalIgnoreCase) >= 0)) n += " " + sh;
            return n.Length > 30 ? n.Substring(0, 29) + "…" : n;
        }
        public static string LabelOf(string key)
        {
            foreach (string[] sp in Specials) if (sp[0] == key) return sp[1];
            Sn s = Find(key);
            if (s != null) return LabelOf(s);
            int i = key.LastIndexOf("~:~");
            return (i >= 0 ? key.Substring(i + 3) : key) + " (indisponível)";
        }
        public static string ValueOf(Sn s)
        {
            float? v = s.Last; if (!v.HasValue) return "-";
            string f = "0";
            if (s.Type == "Voltage" || s.Type == "Current") f = "0.00"; else if (s.Type == "Power" || s.Type == "Data" || s.Type == "SmallData") f = "0.#";
            return v.Value.ToString(f, CultureInfo.InvariantCulture) + " " + Sn.Unit(s.Type);
        }

        static bool Covers(IntPtr fg)
        {
            RECT r; if (!GetWindowRect(fg, out r)) return false;
            Rectangle b = Screen.FromHandle(fg).Bounds;
            return r.L <= b.Left + 2 && r.T <= b.Top + 2 && r.R >= b.Right - 2 && r.B >= b.Bottom - 2;
        }

        static void Tick()
        {
            if (!On && !Editing) { if (win != null && win.Visible) win.Hide(); if (FpsMon.Running) FpsMon.Stop(); return; }
            if (!FpsMon.Running && (DateTime.UtcNow - lastTry).TotalSeconds > 10) { lastTry = DateTime.UtcNow; FpsMon.Start(); }
            // sensores seguem o intervalo do menu Sensores; o FPS e recalculado a cada 500 ms
            if (tm != null) tm.Interval = Math.Max(100, Math.Min(500, SensorHub.IntervalMs));
            IntPtr fg = GetForegroundWindow(); uint pid = 0; GetWindowThreadProcessId(fg, out pid);
            if (pid != fpsPid || (DateTime.UtcNow - fpsAt).TotalMilliseconds >= 480)
            {
                fpsPid = pid; fpsAt = DateTime.UtcNow; fpsApi = null; fpsVal = 0;
                bool ownRender = pid == myPid && Control.FromHandle(fg) is RenderForm;   // janela de render do Benchmark (OpenGL) tambem e medida
                fpsGame = (pid != myPid || ownRender) && pid != 0 && FpsMon.Get(pid, out fpsVal, out fpsApi);
                if (ownRender && fpsGame && ((RenderForm)Control.FromHandle(fg)).Gl != null) fpsApi = "OpenGL";   // o proprio render sabe qual API usa (o processo tambem carrega D3D11 por causa da interface)
            }
            int fps = fpsVal; string api = fpsApi; bool game = fpsGame;
            if (Hidden && !Editing) { if (win != null && win.Visible) win.Hide(); return; }
            bool show = Editing || Mode == 2 || (game && (Mode == 1 || Covers(fg)));
            if (!show) { if (win != null && win.Visible) win.Hide(); return; }
            Screen sc = Editing ? (editScreen ?? Screen.PrimaryScreen) : (game ? Screen.FromHandle(fg) : Screen.PrimaryScreen);
            List<Row> rows = new List<Row>();
            foreach (string k in Items)
            {
                Row r = new Row(); r.Key = k;
                if (k == "@FPS") { r.Label = api ?? "FPS"; r.Value = game ? fps + " FPS" : "-- FPS"; }
                else
                {
                    Sn s = Find(k); r.Label = LabelOf(k); r.Value = s != null ? ValueOf(s) : "-";
                    r.Hot = HotTemp > 0 && s != null && s.Type == "Temperature" && s.Last.HasValue && s.Last.Value >= HotTemp;
                }
                string nm; if (Names.TryGetValue(k, out nm)) r.Label = nm;
                Color cc; if (Colors.TryGetValue(k, out cc)) { r.HasCol = true; r.Col = cc; }
                rows.Add(r);
            }
            if (win == null || win.IsDisposed) win = new OverlayForm();
            win.SetState(rows, Scale, Opac, sc.Bounds, Editing);
            if (!win.Visible) win.Show();
            win.KeepOnTop();
        }
    }

    class OverlayForm : Form
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20;
        [StructLayout(LayoutKind.Sequential)] struct PT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] struct BLEND { public byte Op, Flags, Alpha, Fmt; }
        [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr hdcDst, ref PT pDst, ref SZ size, IntPtr hdcSrc, ref PT pSrc, int key, ref BLEND bl, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        int bgAlpha = 217;
        static Bitmap measBmp = new Bitmap(1, 1); static Graphics measG;
        static float TW(string t, Font f)
        {
            if (measG == null) { measG = Graphics.FromImage(measBmp); measG.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; }
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic)) { sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces; return measG.MeasureString(t ?? "", f, 4000, sf).Width + 2; }
        }
        static int shdOff = 1;
        static void DT(Graphics g, string t, Font f, Rectangle r, Color c, StringAlignment al)
        {
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            using (SolidBrush b = new SolidBrush(c))
            {
                sf.Alignment = al; sf.LineAlignment = StringAlignment.Center; sf.FormatFlags |= StringFormatFlags.NoWrap; sf.Trimming = StringTrimming.None;
                // contorno preto em volta de todos os textos do overlay (legivel sobre qualquer fundo)
                using (SolidBrush sb = new SolidBrush(Color.Black))
                    for (int dx = -shdOff; dx <= shdOff; dx++) for (int dy = -shdOff; dy <= shdOff; dy++)
                        if ((dx != 0 || dy != 0) && (Math.Abs(dx) == shdOff || Math.Abs(dy) == shdOff)) g.DrawString(t ?? "", f, sb, new Rectangle(r.X + dx, r.Y + dy, r.Width, r.Height), sf);
                g.DrawString(t ?? "", f, b, r, sf);
            }
        }
        // o fundo e translucido (por pixel); os textos ficam sempre 100% opacos
        void Render()
        {
            if (!IsHandleCreated || !Visible || Width < 2 || Height < 2) return;
            IntPtr scr0 = IntPtr.Zero, mem = IntPtr.Zero, hb = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                using (Bitmap bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) Draw(g);
                    scr0 = GetDC(IntPtr.Zero); mem = CreateCompatibleDC(scr0);
                    hb = bmp.GetHbitmap(Color.FromArgb(0));
                    old = SelectObject(mem, hb);
                    PT dst = new PT(); dst.x = Left; dst.y = Top; PT src = new PT(); SZ sz = new SZ(); sz.cx = Width; sz.cy = Height;
                    BLEND bl = new BLEND(); bl.Op = 0; bl.Flags = 0; bl.Alpha = 255; bl.Fmt = 1;   // AC_SRC_OVER, AC_SRC_ALPHA
                    UpdateLayeredWindow(Handle, scr0, ref dst, ref sz, mem, ref src, 0, ref bl, 2);
                }
            }
            catch (Exception) { }
            finally
            {
                if (mem != IntPtr.Zero && old != IntPtr.Zero) SelectObject(mem, old);
                if (hb != IntPtr.Zero) DeleteObject(hb);
                if (mem != IntPtr.Zero) DeleteDC(mem);
                if (scr0 != IntPtr.Zero) ReleaseDC(IntPtr.Zero, scr0);
            }
        }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) { ApplyClickThru(); Render(); } }

        List<Overlay.Row> rows = new List<Overlay.Row>(); List<Rectangle> rowR = new List<Rectangle>();
        Rectangle hdrR, btnR, scr; bool edit, clickThru = true;
        Font fl, fv; string fkey = "";
        int scale = 100, margin = 8;
        // arrastar
        bool winDrag; Point dragCur, dragLoc; int rowDrag = -1, dropIdx = -1; Point downPt;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = cBg; Size = new Size(120, 40);
        }
        // o overlay e sempre escuro, independente do tema do app
        static readonly Color cBg = Color.FromArgb(18, 18, 22), cBorder = Color.FromArgb(70, 74, 84), cLabel = Color.FromArgb(165, 171, 184), cValue = Color.FromArgb(240, 242, 246),
            cHot = Color.FromArgb(255, 90, 80), cAccent = Color.FromArgb(40, 95, 185), cGood = Color.FromArgb(35, 160, 70), cGrip = Color.FromArgb(120, 126, 138), cDrop = Color.FromArgb(255, 200, 60);
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000 | 0x80000; return cp; }   // TOOLWINDOW | NOACTIVATE | LAYERED
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyClickThru(); }
        void ApplyClickThru()
        {
            if (!IsHandleCreated) return;
            int ex = GetWindowLong(Handle, GWL_EXSTYLE);
            int want = clickThru ? (ex | WS_EX_TRANSPARENT | 0x80000) : (ex & ~WS_EX_TRANSPARENT);
            if (want == ex) return;
            SetWindowLong(Handle, GWL_EXSTYLE, want);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x37);   // NOMOVE | NOSIZE | NOZORDER | NOACTIVATE | FRAMECHANGED: o Windows relê o estilo
        }
        public void KeepOnTop() { if (IsHandleCreated) { SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x13); ApplyClickThru(); } }
        // fora do modo de edicao a janela nunca deve capturar o mouse (vale tambem para janelas do proprio programa)
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 && !edit) { m.Result = (IntPtr)(-1); return; }   // WM_NCHITTEST -> HTTRANSPARENT
            if (m.Msg == 0x21 && !edit) { m.Result = (IntPtr)3; return; }       // WM_MOUSEACTIVATE -> MA_NOACTIVATE: um clique nunca rouba o foco do jogo
            base.WndProc(ref m);
            // se algo (o Windows, o jogo) mexer no estilo da janela, o clique-atraves e reafirmado na hora
            if ((m.Msg == 0x7D || m.Msg == 0x47 || m.Msg == 0x18) && !edit && IsHandleCreated) ApplyClickThru();   // WM_STYLECHANGED, WM_WINDOWPOSCHANGED, WM_SHOWWINDOW
        }

        void Fonts()
        {
            string k = "Segoe UI|" + scale;
            if (k == fkey && fl != null) return;
            fkey = k; if (fl != null) { fl.Dispose(); fv.Dispose(); }
            float sz = 9.5f * scale / 100f;
            fl = new Font("Segoe UI", sz, FontStyle.Regular); fv = new Font("Segoe UI", sz, FontStyle.Bold);
        }

        public void SetState(List<Overlay.Row> r, int sc, int op, Rectangle screen, bool ed)
        {
            rows = r; scale = sc; scr = screen; edit = ed;
            Fonts();
            LD d = CurLay(ed);
            float s = sc / 100f; int pad = (int)(9 * s), gap = Overlay.Compact ? 0 : (int)(16 * s), rh = fl.Height + (int)(5 * s);
            int lw = 0, vw = 0;
            foreach (Overlay.Row x in r)
            {
                if (!Overlay.Compact) lw = Math.Max(lw, (int)Math.Ceiling(TW(x.Label, fl)));
                vw = Math.Max(vw, (int)Math.Ceiling(TW(x.Value, fv)));
            }
            if (!Overlay.Compact && !d.Dot.IsEmpty) lw += (int)(9 * s);
            if (!d.Pill.IsEmpty) vw += (int)(10 * s);
            int grip = ed ? (int)(14 * s) : 0;
            int w = Math.Max((int)(110 * s), pad * 2 + grip + lw + gap + vw);
            if (ed) w = Math.Max(w, (int)(250 * s));
            else if (d.Hdr != null) w = Math.Max(w, (int)Math.Ceiling(TW(d.Hdr, fv)) + pad * 2 + 4);
            int y = pad - (int)(2 * s);
            hdrR = Rectangle.Empty; btnR = Rectangle.Empty;
            hdrH = 0;
            if (ed) { hdrR = new Rectangle(0, 0, w, (int)(22 * s)); y = hdrR.Height + (int)(4 * s); }
            else if (d.Hdr != null) { hdrH = fv.Height + (int)(4 * s); y += hdrH + (int)(3 * s); }
            rowR.Clear();
            for (int i = 0; i < r.Count; i++) { rowR.Add(new Rectangle(pad, y, w - pad * 2, rh)); y += rh; }
            if (r.Count == 0) y += rh;
            if (ed)
            {
                y += (int)(4 * s);
                btnR = new Rectangle(pad, y, w - pad * 2, (int)(24 * s)); y += btnR.Height + (int)(4 * s);
            }
            else y += pad - (int)(2 * s);
            bgAlpha = Math.Max(50, Math.Min(100, op)) * 255 / 100;
            clickThru = !ed; ApplyClickThru();   // reafirmado a cada atualizacao: o estado nunca fica desencontrado
            if (!winDrag)
            {
                int rx = Math.Max(0, scr.Width - w - 2 * margin), ry = Math.Max(0, scr.Height - y - 2 * margin);
                int nx = scr.Left + margin + (int)Math.Round(Overlay.Fx * rx), ny = scr.Top + margin + (int)Math.Round(Overlay.Fy * ry);
                if (Left != nx || Top != ny || Width != w || Height != y) SetBounds(nx, ny, w, y);
            }
            Render();
        }

        static GraphicsPath Round(Rectangle r, int d)
        {
            GraphicsPath p = new GraphicsPath();
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        // ---- layouts do overlay (independentes do tema do programa; os 7 "Tema:" usam a paleta de cada tema) ----
        public class LD
        {
            public string Name, Hdr; public int Rad = -1, BarPos, CornerStyle; public float BW = 1f;
            public Color Top, Bot, Bd, Bar, Bar2, Lab, Val, Glow, HdrA, HdrB, HdrTx, Line, Zebra, Inner, Corner, Pill, PillBd, Dot;
            public bool Bevel, Soft, Gloss, Scan;
        }
        static LD CurLay(bool ed) { return Layouts[ed ? 0 : Math.Max(0, Math.Min(Layouts.Length - 1, Overlay.Layout))]; }
        static Color Sc(Color c, int bg) { return Color.FromArgb(Math.Max(0, Math.Min(255, c.A * bg / 255)), c); }
        int hdrH;
        static Color K(int r, int g, int b) { return Color.FromArgb(r, g, b); }
        static Color KA(int a, int r, int g, int b) { return Color.FromArgb(a, r, g, b); }
        static LD N(List<LD> l, string n) { LD d = new LD(); d.Name = n; l.Add(d); return d; }
        static void BevelBox(Graphics g, Rectangle r)
        {
            SmoothingMode sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
            using (Pen w = new Pen(Color.White)) using (Pen k = new Pen(Color.Black)) using (Pen gr = new Pen(Color.FromArgb(128, 128, 128)))
            {
                g.DrawLine(w, 0, 0, r.Right, 0); g.DrawLine(w, 0, 0, 0, r.Bottom);
                g.DrawLine(k, 0, r.Bottom, r.Right, r.Bottom); g.DrawLine(k, r.Right, 0, r.Right, r.Bottom);
                g.DrawLine(w, 1, 1, r.Right - 1, 1); g.DrawLine(w, 1, 1, 1, r.Bottom - 1);
                g.DrawLine(gr, 1, r.Bottom - 1, r.Right - 1, r.Bottom - 1); g.DrawLine(gr, r.Right - 1, 1, r.Right - 1, r.Bottom - 1);
            }
            g.SmoothingMode = sm;
        }
        public static readonly LD[] Layouts = BuildLayouts();
        static LD[] BuildLayouts()
        {
            List<LD> l = new List<LD>(); LD d; Pal p;
            d = N(l, "Padrão (fundo + contorno)"); d.Top = d.Bot = cBg; d.Bd = cBorder; d.Soft = true; d.Line = KA(30, 255, 255, 255); d.Gloss = true;
            d = N(l, "Sem fundo nem contorno"); d.Dot = K(0, 229, 255);
            d = N(l, "So contorno (sem fundo)"); d.Bd = K(235, 238, 245); d.BW = 2f; d.Inner = KA(110, 255, 255, 255); d.Corner = K(255, 255, 255); d.CornerStyle = 1;
            d = N(l, "Barra lateral"); d.Top = d.Bot = cBg; d.Rad = 0; d.Bar = K(0, 190, 255); d.Bar2 = K(0, 90, 220); d.BarPos = 1; d.Zebra = KA(18, 255, 255, 255); d.Line = KA(36, 255, 255, 255); d.Dot = K(0, 190, 255);
            d = N(l, "Neon"); d.Top = K(10, 6, 24); d.Bot = K(24, 6, 46); d.Bd = K(0, 229, 255); d.Glow = K(0, 229, 255); d.Hdr = "NEON"; d.HdrA = K(0, 120, 150); d.HdrB = K(10, 6, 24); d.HdrTx = K(0, 245, 255); d.Inner = KA(90, 255, 0, 200); d.Dot = K(255, 0, 200); d.Rad = 8; d.Val = K(230, 250, 255); d.Lab = K(130, 170, 200);
            d = N(l, "Terminal (verde)"); d.Top = d.Bot = K(0, 6, 0); d.Rad = 0; d.Bd = K(0, 200, 70); d.Hdr = "C:\\> monitor_"; d.HdrA = K(0, 90, 30); d.HdrB = K(0, 50, 16); d.HdrTx = K(110, 255, 140); d.Scan = true; d.Line = KA(70, 0, 200, 70); d.Corner = K(0, 255, 90); d.CornerStyle = 1; d.Lab = K(50, 170, 80); d.Val = K(110, 255, 140);
            // temas do programa (cores vindas da paleta de cada tema, nao do tema ativo)
            p = Pal.Make(0); d = N(l, "Tema: Luna (XP)"); d.Top = d.Bot = p.Face; d.Bd = p.CapFrame; d.BW = 2f; d.Hdr = "BunoTunner"; d.HdrA = p.CapA; d.HdrB = p.CapB; d.HdrTx = p.CapText; d.Zebra = KA(120, 255, 255, 255); d.Line = p.GroupLine; d.Lab = K(70, 70, 70); d.Val = p.Text; d.Dot = p.OptMark; d.Rad = 6;
            p = Pal.Make(1); d = N(l, "Tema: Frutiger Aero (Win 7)"); d.Top = K(250, 252, 255); d.Bot = p.PaneTop; d.Bd = p.Border; d.Rad = 16; d.Hdr = "Sensores"; d.HdrA = K(226, 240, 252); d.HdrB = K(150, 200, 238); d.HdrTx = p.PaneText; d.Gloss = true; d.Inner = KA(170, 255, 255, 255); d.Pill = KA(170, 255, 255, 255); d.PillBd = KA(110, 110, 140, 180); d.Lab = K(60, 80, 110); d.Val = p.GroupText;
            p = Pal.Make(2); d = N(l, "Tema: Dark / AMOLED"); d.Top = d.Bot = p.Face; d.Bd = p.Border; d.Rad = 10; d.Hdr = "SENSORES"; d.HdrA = p.CapA; d.HdrB = p.CapB; d.HdrTx = p.CapText; d.Bar = p.GroupText; d.BarPos = 1; d.Dot = p.GroupText; d.Line = KA(40, 255, 255, 255); d.Pill = K(30, 30, 38); d.Lab = p.Dim; d.Val = p.Text;
            p = Pal.Make(3); d = N(l, "Tema: Neon / Cyberpunk"); d.Top = d.Bot = p.Face; d.Bd = p.Border; d.Glow = p.GroupText; d.Rad = 3; d.Hdr = "CYBER//SYS"; d.HdrA = p.CapA; d.HdrB = p.CapB; d.HdrTx = Color.White; d.Corner = p.Border; d.CornerStyle = 1; d.Scan = true; d.Dot = p.GroupText; d.Zebra = KA(46, 180, 0, 200); d.Bar = p.CapA; d.Bar2 = p.Border; d.BarPos = 3; d.Lab = p.Dim; d.Val = p.Text;
            p = Pal.Make(4); d = N(l, "Tema: Steampunk"); d.Top = p.Face; d.Bot = K(150, 96, 52); d.Bd = p.CapFrame; d.BW = 2f; d.Rad = 8; d.Hdr = "VAPOR"; d.HdrA = p.CapA; d.HdrB = p.CapB; d.HdrTx = p.CapText; d.Inner = KA(200, 236, 190, 110); d.Corner = K(236, 190, 110); d.CornerStyle = 2; d.Pill = KA(210, 248, 238, 212); d.PillBd = K(74, 44, 16); d.Line = KA(90, 74, 44, 16); d.Lab = p.Dim; d.Val = p.Text;
            p = Pal.Make(5); d = N(l, "Tema: Vintage (Win 95)"); d.Top = d.Bot = p.Face; d.Rad = 0; d.Bevel = true; d.Hdr = "BunoTunner"; d.HdrA = p.CapA; d.HdrB = p.CapB; d.HdrTx = Color.White; d.Pill = Color.White; d.PillBd = K(128, 128, 128); d.Lab = K(64, 64, 64); d.Val = Color.Black;
            p = Pal.Make(6); d = N(l, "Tema: GTA VI (Vice City)"); d.Top = K(120, 30, 110); d.Bot = K(210, 70, 100); d.Bd = p.Border; d.BW = 2f; d.Hdr = "VICE CITY"; d.HdrA = K(255, 150, 60); d.HdrB = K(210, 40, 130); d.HdrTx = K(255, 246, 230); d.Bar = p.Hot; d.Bar2 = K(255, 110, 150); d.BarPos = 3; d.Inner = KA(110, 255, 200, 120); d.Dot = p.Hot; d.Pill = KA(110, 0, 0, 0); d.Gloss = true; d.Line = KA(50, 255, 220, 200); d.Lab = p.Dim; d.Val = p.Text;
            // outros estilos
            d = N(l, "Vidro (translúcido)"); d.Top = d.Bot = KA(110, 255, 255, 255); d.Bd = KA(170, 255, 255, 255); d.Rad = 14; d.Gloss = true; d.Inner = KA(90, 255, 255, 255); d.Pill = KA(60, 255, 255, 255); d.Lab = K(230, 235, 245); d.Val = Color.White;
            d = N(l, "Pílula"); d.Top = d.Bot = cBg; d.Rad = 60; d.Bd = K(120, 126, 140); d.Dot = K(120, 200, 255); d.Pill = K(38, 42, 56); d.Gloss = true;
            d = N(l, "Claro minimalista"); d.Top = K(250, 251, 253); d.Bot = K(232, 235, 242); d.Bd = K(190, 194, 204); d.Hdr = "Sensores"; d.HdrA = K(238, 240, 246); d.HdrB = K(214, 218, 228); d.HdrTx = K(60, 66, 80); d.Zebra = KA(14, 0, 0, 0); d.Line = KA(40, 0, 0, 0); d.Pill = KA(22, 0, 0, 0); d.Lab = K(100, 106, 120); d.Val = K(25, 28, 36);
            d = N(l, "Barra superior"); d.Top = d.Bot = cBg; d.Rad = 8; d.Bar = K(255, 140, 0); d.Bar2 = K(255, 210, 60); d.BarPos = 2; d.Zebra = KA(20, 255, 255, 255); d.Dot = K(255, 170, 30); d.Line = KA(28, 255, 255, 255);
            d = N(l, "Barra inferior"); d.Top = d.Bot = cBg; d.Rad = 8; d.Bar = K(60, 200, 110); d.Bar2 = K(40, 150, 220); d.BarPos = 3; d.Pill = K(28, 58, 42); d.Dot = K(60, 200, 110); d.Line = KA(28, 255, 255, 255);
            d = N(l, "Âmbar (monitor antigo)"); d.Top = d.Bot = K(10, 6, 0); d.Rad = 0; d.Bd = K(255, 176, 0); d.Hdr = "READY_"; d.HdrA = d.HdrB = K(255, 176, 0); d.HdrTx = K(0, 0, 0); d.Scan = true; d.Corner = K(255, 176, 0); d.CornerStyle = 1; d.Line = KA(70, 255, 176, 0); d.Lab = K(190, 120, 0); d.Val = K(255, 190, 40);
            d = N(l, "HUD ciano"); d.Top = d.Bot = K(2, 22, 32); d.Rad = 0; d.Bd = K(0, 150, 180); d.Corner = K(0, 229, 255); d.CornerStyle = 1; d.Bar = K(0, 229, 255); d.BarPos = 1; d.Hdr = "SYS//HUD"; d.HdrA = K(0, 80, 100); d.HdrB = K(0, 30, 44); d.HdrTx = K(0, 229, 255); d.Scan = true; d.Zebra = KA(30, 0, 229, 255); d.Lab = K(90, 170, 190); d.Val = K(200, 250, 255);
            d = N(l, "Oceano"); d.Top = K(0, 70, 120); d.Bot = K(0, 20, 50); d.Bd = K(0, 170, 230); d.Rad = 24; d.Gloss = true; d.Inner = KA(80, 150, 220, 255); d.Hdr = "SENSORES"; d.HdrA = K(0, 130, 190); d.HdrB = K(0, 60, 110); d.HdrTx = Color.White; d.Dot = K(80, 220, 255); d.Pill = KA(50, 255, 255, 255); d.Lab = K(160, 205, 230); d.Val = Color.White;
            d = N(l, "Floresta"); d.Top = K(26, 70, 36); d.Bot = K(8, 26, 14); d.Bd = K(100, 190, 110); d.Rad = 8; d.Hdr = "SENSORES"; d.HdrA = K(64, 136, 76); d.HdrB = K(26, 70, 36); d.HdrTx = K(235, 250, 235); d.Inner = KA(80, 160, 230, 170); d.Zebra = KA(24, 255, 255, 255); d.Line = KA(50, 150, 220, 160); d.Dot = K(140, 230, 150); d.Lab = K(160, 205, 165); d.Val = K(235, 250, 235);
            d = N(l, "Rosa"); d.Top = K(100, 24, 70); d.Bot = K(36, 8, 28); d.Bd = K(255, 120, 180); d.Glow = K(255, 120, 180); d.Hdr = "SENSORES"; d.HdrA = K(240, 90, 160); d.HdrB = K(150, 30, 100); d.HdrTx = Color.White; d.Gloss = true; d.Dot = K(255, 150, 200); d.Pill = K(70, 20, 50); d.Lab = K(235, 175, 205); d.Val = Color.White;
            d = N(l, "Papel"); d.Top = d.Bot = K(250, 244, 225); d.Bd = K(150, 125, 85); d.Rad = 4; d.Line = K(190, 205, 230); d.Bar = K(225, 95, 95); d.BarPos = 1; d.Lab = K(120, 100, 70); d.Val = K(50, 34, 16);
            d = N(l, "Faixa escura (sem borda)"); d.Top = d.Bot = K(0, 0, 0); d.Rad = 0; d.Bar = K(0, 229, 255); d.Bar2 = K(255, 0, 200); d.BarPos = 2; d.Line = KA(40, 255, 255, 255); d.Dot = Color.White;
            return l.ToArray();
        }

        void Draw(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Fonts(); float s = scale / 100f;
            g.Clear(Color.Transparent);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            LD d = CurLay(edit);   // na edicao sempre mostra a caixa padrao para dar para arrastar
            shdOff = Math.Max(1, (int)Math.Round(s));
            int rad = d.Rad < 0 ? (Xp.Rad + 2) * 2 : Math.Min(d.Rad, Math.Min(Width, Height) - 1);
            using (GraphicsPath gp = Round(r, rad))
            {
                if (!d.Top.IsEmpty)
                {
                    using (LinearGradientBrush lb = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Sc(d.Top, bgAlpha), Sc(d.Bot, bgAlpha), 90f)) g.FillPath(lb, gp);
                }
                if (!edit)
                {
                    if (!d.Glow.IsEmpty) using (Pen gl = new Pen(Color.FromArgb(70, d.Glow), 5f)) g.DrawPath(gl, gp);
                    g.SetClip(gp);
                    for (int i = 0; i < rowR.Count && i < rows.Count; i++)
                    {
                        Rectangle rr = rowR[i];
                        if (!d.Zebra.IsEmpty && (i & 1) == 1) using (SolidBrush zb = new SolidBrush(Sc(d.Zebra, bgAlpha))) g.FillRectangle(zb, 1, rr.Top, Width - 2, rr.Height);
                        if (!d.Line.IsEmpty && i < rows.Count - 1) using (Pen lp = new Pen(Sc(d.Line, bgAlpha))) g.DrawLine(lp, rr.Left, rr.Bottom, rr.Right, rr.Bottom);
                    }
                    if (hdrH > 0 && d.Hdr != null)
                    {
                        Rectangle hr = d.Bevel ? new Rectangle(3, 3, Width - 7, hdrH - 3) : new Rectangle(0, 0, Width, hdrH);
                        using (LinearGradientBrush hb = new LinearGradientBrush(hr, Sc(d.HdrA, bgAlpha), Sc(d.HdrB, bgAlpha), 90f)) g.FillRectangle(hb, hr);
                        if (!d.Bd.IsEmpty && !d.Bevel) using (Pen hp = new Pen(Color.FromArgb(120, d.Bd))) g.DrawLine(hp, 0, hdrH - 1, Width, hdrH - 1);
                        DT(g, d.Hdr, fv, hr, d.HdrTx, StringAlignment.Center);
                    }
                    if (d.Gloss) using (LinearGradientBrush gb2 = new LinearGradientBrush(new Rectangle(0, 0, Width, Math.Max(2, Height / 2)), Sc(KA(70, 255, 255, 255), bgAlpha), Color.FromArgb(0, 255, 255, 255), 90f)) g.FillRectangle(gb2, 0, 0, Width, Math.Max(2, Height / 2));
                    if (d.Scan) using (Pen sp2 = new Pen(Sc(KA(60, 0, 0, 0), bgAlpha))) for (int yy = 1; yy < Height; yy += 3) g.DrawLine(sp2, 0, yy, Width, yy);
                    if (d.BarPos > 0)
                    {
                        int t = Math.Max(3, (int)(4 * s));
                        Rectangle br = d.BarPos == 1 ? new Rectangle(0, 0, t, Height) : d.BarPos == 2 ? new Rectangle(0, 0, Width, t) : new Rectangle(0, Height - t - 1, Width, t + 1);
                        if (d.Bar2.IsEmpty) using (SolidBrush ab = new SolidBrush(d.Bar)) g.FillRectangle(ab, br);
                        else using (LinearGradientBrush ab = new LinearGradientBrush(br, d.Bar, d.Bar2, d.BarPos == 1 ? 90f : 0f)) g.FillRectangle(ab, br);
                    }
                    g.ResetClip();
                    if (d.Bevel) BevelBox(g, r);
                    if (!d.Inner.IsEmpty)
                    {
                        Rectangle ir = new Rectangle(3, hdrH + 3, Math.Max(2, Width - 7), Math.Max(2, Height - hdrH - 7));
                        using (GraphicsPath ip = Round(ir, Math.Max(0, rad - 6))) using (Pen inp = new Pen(d.Inner)) g.DrawPath(inp, ip);
                    }
                    if (!d.Corner.IsEmpty)
                    {
                        int ln = Math.Max(5, (int)(8 * s)), x0 = 3, y0 = hdrH + 3, x1 = Width - 4, y1 = Height - 4;
                        using (Pen cp = new Pen(d.Corner, 2f))
                        {
                            if (d.CornerStyle == 2)
                            {
                                int rv = Math.Max(2, (int)(2.5 * s));
                                using (SolidBrush rb = new SolidBrush(d.Corner))
                                    foreach (Point q in new Point[] { new Point(x0 + 4, y0 + 4), new Point(x1 - 4, y0 + 4), new Point(x0 + 4, y1 - 4), new Point(x1 - 4, y1 - 4) })
                                    { g.FillEllipse(rb, q.X - rv, q.Y - rv, rv * 2, rv * 2); g.DrawEllipse(Pens.Black, q.X - rv, q.Y - rv, rv * 2, rv * 2); }
                            }
                            else
                            {
                                g.DrawLine(cp, x0, y0, x0 + ln, y0); g.DrawLine(cp, x0, y0, x0, y0 + ln); g.DrawLine(cp, x1, y0, x1 - ln, y0); g.DrawLine(cp, x1, y0, x1, y0 + ln);
                                g.DrawLine(cp, x0, y1, x0 + ln, y1); g.DrawLine(cp, x0, y1, x0, y1 - ln); g.DrawLine(cp, x1, y1, x1 - ln, y1); g.DrawLine(cp, x1, y1, x1, y1 - ln);
                            }
                        }
                    }
                }
                if (edit) using (Pen ep = new Pen(cAccent, 2f)) { ep.DashStyle = DashStyle.Dash; g.DrawPath(ep, gp); }
                else if (!d.Bd.IsEmpty && !d.Bevel) using (Pen bp = new Pen(Color.FromArgb(d.Soft ? bgAlpha : 255, d.Bd), d.BW)) g.DrawPath(bp, gp);
            }
            int grip = edit ? (int)(14 * s) : 0;
            if (edit)
            {
                using (SolidBrush b = new SolidBrush(cAccent)) g.FillRectangle(b, 1, 1, Width - 2, hdrR.Height);
                DT(g, Lang.T("Arraste aqui para mover"), fl, hdrR, Color.White, StringAlignment.Center);
            }
            for (int i = 0; i < rows.Count && i < rowR.Count; i++)
            {
                Rectangle rr = rowR[i]; Overlay.Row x = rows[i];
                if (edit)
                {
                    using (Pen sp = new Pen(Color.FromArgb(60, 70, 80))) g.DrawLine(sp, rr.Left, rr.Bottom, rr.Right, rr.Bottom);
                    using (SolidBrush gb = new SolidBrush(cGrip))
                        for (int k = 0; k < 3; k++) g.FillRectangle(gb, rr.Left + 1, rr.Top + rr.Height / 2 - (int)(4 * s) + k * (int)(4 * s), (int)(8 * s), Math.Max(1, (int)(2 * s)));
                }
                int dotW = (!Overlay.Compact && !d.Dot.IsEmpty) ? (int)(9 * s) : 0;
                Rectangle lr = new Rectangle(rr.Left + grip + dotW, rr.Top, rr.Width - grip - dotW, rr.Height);
                if (dotW > 0)
                {
                    int ds = Math.Max(4, (int)(5 * s));
                    using (SolidBrush db = new SolidBrush(x.Hot ? cHot : d.Dot)) g.FillEllipse(db, rr.Left + grip + 1, rr.Top + (rr.Height - ds) / 2, ds, ds);
                    g.DrawEllipse(Pens.Black, rr.Left + grip + 1, rr.Top + (rr.Height - ds) / 2, ds, ds);
                }
                int pp = d.Pill.IsEmpty ? 0 : (int)(5 * s);
                Rectangle vr = new Rectangle(lr.Left + (Overlay.Compact ? pp : 0), lr.Top, lr.Width - pp - (Overlay.Compact ? pp : 0), lr.Height);
                if (pp > 0)
                {
                    int tw = (int)Math.Ceiling(TW(x.Value, fv));
                    Rectangle pr = new Rectangle(Overlay.Compact ? lr.Left : lr.Right - pp * 2 - tw, rr.Top + 1, tw + pp * 2, Math.Max(4, rr.Height - 2));
                    using (GraphicsPath pg = Round(pr, Math.Min(pr.Height, 12)))
                    {
                        using (SolidBrush pb = new SolidBrush(Sc(d.Pill, bgAlpha))) g.FillPath(pb, pg);
                        if (!d.PillBd.IsEmpty) using (Pen pbp = new Pen(d.PillBd)) g.DrawPath(pbp, pg);
                    }
                }
                Color vc = x.Hot ? cHot : (x.HasCol ? x.Col : (d.Val.IsEmpty ? cValue : d.Val));
                Color lc = (Overlay.ColName && x.HasCol && !x.Hot) ? x.Col : (d.Lab.IsEmpty ? cLabel : d.Lab);
                if (!Overlay.Compact) DT(g, x.Label, fl, lr, lc, StringAlignment.Near);
                DT(g, x.Value, fv, vr, vc, Overlay.Compact ? StringAlignment.Near : StringAlignment.Far);
            }
            if (edit)
            {
                if (rows.Count == 0) DT(g, Lang.T("(vazio - adicione sensores no menu)"), fl, new Rectangle(8, hdrR.Height + 4, Width - 16, (int)(20 * s)), cLabel, StringAlignment.Near);
                if (dropIdx >= 0 && rowDrag >= 0 && rowR.Count > 0)
                {
                    int yy = dropIdx < rowR.Count ? rowR[dropIdx].Top : rowR[rowR.Count - 1].Bottom;
                    using (Pen dp = new Pen(cDrop, 2f)) g.DrawLine(dp, 6, yy, Width - 6, yy);
                }
                using (GraphicsPath bp = Round(btnR, 6))
                {
                    using (SolidBrush bb = new SolidBrush(cGood)) g.FillPath(bb, bp);
                }
                DT(g, Lang.T("Concluir"), fv, btnR, Color.White, StringAlignment.Center);
            }
        }

        int RowAt(Point p) { for (int i = 0; i < rowR.Count; i++) if (p.Y >= rowR[i].Top && p.Y < rowR[i].Bottom) return i; return -1; }
        int DropAt(Point p)
        {
            for (int i = 0; i < rowR.Count; i++) if (p.Y < rowR[i].Top + rowR[i].Height / 2) return i;
            return rowR.Count;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!edit) return;
            if (e.Button == MouseButtons.Right) { int i = RowAt(e.Location); if (i >= 0) Overlay.RemoveAt(i); return; }
            if (e.Button != MouseButtons.Left) return;
            if (btnR.Contains(e.Location)) { Overlay.SetEditing(false, null); return; }
            downPt = e.Location;
            int ri = RowAt(e.Location);
            if (ri >= 0) { rowDrag = ri; dropIdx = ri; }
            else { winDrag = true; dragCur = Cursor.Position; dragLoc = Location; }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!edit) return;
            if (winDrag)
            {
                Point c = Cursor.Position;
                int nx = Math.Max(scr.Left + margin, Math.Min(scr.Right - Width - margin, dragLoc.X + c.X - dragCur.X));
                int ny = Math.Max(scr.Top + margin, Math.Min(scr.Bottom - Height - margin, dragLoc.Y + c.Y - dragCur.Y));
                Location = new Point(nx, ny);
                int rx = scr.Width - Width - 2 * margin, ry = scr.Height - Height - 2 * margin;
                Overlay.Fx = rx > 0 ? Math.Max(0, Math.Min(1, (double)(nx - scr.Left - margin) / rx)) : 0;
                Overlay.Fy = ry > 0 ? Math.Max(0, Math.Min(1, (double)(ny - scr.Top - margin) / ry)) : 0;
            }
            else if (rowDrag >= 0) { dropIdx = DropAt(e.Location); Cursor = Cursors.Hand; Render(); }
            else Cursor = (RowAt(e.Location) < 0 && !btnR.Contains(e.Location)) ? Cursors.SizeAll : Cursors.Hand;
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (winDrag) { winDrag = false; Overlay.Commit(); }
            if (rowDrag >= 0)
            {
                int from = rowDrag, to = dropIdx; rowDrag = -1; dropIdx = -1;
                if (Math.Abs(e.Y - downPt.Y) > 3 && to >= 0 && to != from && to != from + 1) Overlay.MoveItem(from, to);
                else Render();
            }
        }
    }

    class OverlayConfigForm : XpForm
    {
        static OverlayConfigForm inst;
        public static void Open(Form owner)
        {
            if (inst != null && !inst.IsDisposed) { if (inst.WindowState == FormWindowState.Minimized) inst.WindowState = FormWindowState.Normal; inst.BringToFront(); return; }
            inst = new OverlayConfigForm(); inst.Show(owner);
        }

        XpOption chkOn, chkCompact, chkColName; ComboBox cbMode, cbSize, cbOp, cbLay, cbHot, cbGrp; XpText txSearch; ListView lvAv; ListBox lbSel; XpButton btnEdit, btnKey; Label hint; Timer tm = new Timer(); bool capturing;
        int dropIdx = -1, downIdx = -1; Point downPt; bool building;
        static int[] Seq(int a, int b) { List<int> l = new List<int>(); for (int v = a; v <= b; v += 5) l.Add(v); return l.ToArray(); }
        static readonly int[] Sizes = Seq(50, 200), Ops = Seq(50, 100);

        public OverlayConfigForm()
        {
            Text = "Overlay em jogos";
            Size = new Size(796, 690); KeyPreview = true;
            Panel body = new Panel(); body.Dock = DockStyle.Fill; Controls.Add(body);

            chkOn = UI.Opt(body, "Ativar overlay em jogos", 8, 8, 260, false);
            UI.Lbl(body, "Tamanho:", 300, 9); cbSize = Combo(body, 362, 5, 70);
            foreach (int v in Sizes) cbSize.Items.Add(v + "%");
            UI.Lbl(body, "Opacidade:", 450, 9); cbOp = Combo(body, 520, 5, 70);
            foreach (int v in Ops) cbOp.Items.Add(v + "%");
            UI.Lbl(body, "Layout:", 600, 9); cbLay = Combo(body, 650, 5, 126); cbLay.DropDownWidth = 230;
            foreach (OverlayForm.LD x in OverlayForm.Layouts) cbLay.Items.Add(x.Name);
            cbLay.MaxDropDownItems = 25;
            UI.Lbl(body, "Mostrar:", 8, 40); cbMode = Combo(body, 66, 36, 420);
            cbMode.Items.Add("Somente em jogos (tela cheia ou janela sem borda)");
            cbMode.Items.Add("Em qualquer programa que desenhe com DirectX");
            cbMode.Items.Add("Sempre (para testar)");
            UI.Lbl(body, "Atalho:", 500, 40);
            btnKey = UI.Btn(body, "", 556, 36, 220, delegate { capturing = true; Overlay.PauseKey(true); btnKey.Text = "Pressione o atalho...  (Esc cancela, Del limpa)"; });
            btnKey.Text = "Mostrar/ocultar: " + Overlay.KeyName() + (Overlay.KeyOk ? "" : " (em uso!)");

            UI.Lbl(body, "Sensores disponíveis  (duplo clique ou arraste para a direita)", 8, 72, Xp.UiBold);
            txSearch = UI.Txt(body, 8, 94, 220, "");
            cbGrp = Combo(body, 234, 94, 146);
            lvAv = new XpList(); lvAv.SetBounds(8, 124, 372, 332); lvAv.View = View.Details; lvAv.FullRowSelect = true; lvAv.HideSelection = false; lvAv.GridLines = true;
            lvAv.Columns.Add("Sensor", 190); lvAv.Columns.Add("Grupo", 100); lvAv.Columns.Add("Valor", 62);
            lvAv.BackColor = Xp.EditBg; lvAv.Font = Xp.Ui; lvAv.AllowDrop = true; UI.DoubleBuffer(lvAv);
            body.Controls.Add(lvAv);

            UI.Btn(body, "+", 388, 250, 34, delegate { AddSel(); });
            UI.Btn(body, "-", 388, 280, 34, delegate { RemSel(); });

            UI.Lbl(body, "Ordem no overlay  (arraste para reordenar)", 430, 72, Xp.UiBold);
            lbSel = new ListBox(); lbSel.SetBounds(430, 94, 346, 362); lbSel.DrawMode = DrawMode.OwnerDrawFixed; lbSel.ItemHeight = 24; lbSel.IntegralHeight = false;
            lbSel.BackColor = Xp.EditBg; lbSel.Font = Xp.Ui; lbSel.AllowDrop = true; UI.DoubleBuffer(lbSel);
            body.Controls.Add(lbSel);

            // ferramentas do item selecionado (lista da direita)
            UI.Btn(body, "Cor...", 430, 463, 66, delegate { PickColor(); });
            UI.Btn(body, "Renomear...", 500, 463, 84, delegate { RenameSel(); });
            UI.Btn(body, "Subir", 588, 463, 56, delegate { MoveSel(-1); });
            UI.Btn(body, "Descer", 648, 463, 58, delegate { MoveSel(1); });
            UI.Btn(body, "Resetar", 710, 463, 66, delegate { ResetSel(); });

            UI.Btn(body, "Desfazer", 8, 497, 76, delegate { Overlay.Undo(); });
            UI.Btn(body, "Refazer", 88, 497, 70, delegate { Overlay.Redo(); });
            UI.Btn(body, "Limpar lista", 162, 497, 96, delegate { if (Overlay.Items.Count == 0) return; Overlay.Items.Clear(); Overlay.Commit(); });
            UI.Btn(body, "Restaurar padrão", 262, 497, 118, delegate
            {
                if (Dlg.Show(this, "Restaurar todas as opções do overlay (itens, cores, nomes, tamanho, opacidade...) para o padrão?\nVocê pode desfazer com Ctrl+Z.", "Overlay em jogos", MessageBoxButtons.YesNo) == DialogResult.Yes) Overlay.ResetAll();
            });
            btnEdit = UI.Btn(body, "Editar na tela", 430, 497, 124, delegate { Overlay.SetEditing(!Overlay.Editing, Screen.FromControl(this)); });
            UI.Btn(body, "Exportar...", 558, 497, 104, delegate { DoExport(); });
            UI.Btn(body, "Importar...", 666, 497, 110, delegate { DoImport(); });

            chkCompact = UI.Opt(body, "Compacto (só os valores, sem nomes)", 8, 535, 270, false);
            chkColName = UI.Opt(body, "Aplicar a cor também ao nome", 290, 535, 230, false);
            UI.Btn(body, "Fechar", 686, 531, 90, delegate { Close(); });
            UI.Lbl(body, "Temperatura em vermelho a partir de:", 8, 568);
            cbHot = Combo(body, 238, 564, 90);
            cbHot.Items.Add("Desligado"); for (int t = 70; t <= 95; t += 5) cbHot.Items.Add(t + " °C");

            hint = UI.Lbl(body, "", 8, 600, Xp.Ui, Xp.Dim); hint.AutoSize = false; hint.SetBounds(8, 598, 768, 46);
            hint.Text = "Dica: Esc fecha esta janela; Ctrl+Z desfaz e Ctrl+Y refaz. Clique com o botão direito num item da lista para mudar cor, nome, ordem ou remover; duplo clique abre a cor. Em \"Editar na tela\" arraste o overlay pelo topo e as linhas para reordenar. Jogos em tela cheia exclusiva (não a sem borda) podem esconder o overlay. O atalho Mostrar/ocultar e global: a combinação escolhida fica reservada para o BunoTunner e não chega aos outros programas (se aparecer \"em uso!\", outro programa já a usa - escolha outra).";

            building = true;
            chkOn.Checked = Overlay.On;
            cbSize.SelectedIndex = Math.Max(0, Array.IndexOf(Sizes, Overlay.Scale));
            cbOp.SelectedIndex = Math.Max(0, Array.IndexOf(Ops, Overlay.Opac));
            cbMode.SelectedIndex = Overlay.Mode; cbLay.SelectedIndex = Overlay.Layout;
            chkCompact.Checked = Overlay.Compact; chkColName.Checked = Overlay.ColName;
            cbGrp.Items.Add("Todos os grupos"); cbGrp.SelectedIndex = 0;
            cbHot.SelectedIndex = Overlay.HotTemp <= 0 ? 0 : Math.Max(1, Math.Min(cbHot.Items.Count - 1, (Overlay.HotTemp - 70) / 5 + 1));
            building = false;
            FillAvail(); FillSel();

            chkOn.CheckedChanged += delegate { if (building) return; Overlay.On = chkOn.Checked; Overlay.Commit(); };
            cbSize.SelectedIndexChanged += delegate { if (building || cbSize.SelectedIndex < 0) return; Overlay.Scale = Sizes[cbSize.SelectedIndex]; Overlay.Commit(); };
            cbOp.SelectedIndexChanged += delegate { if (building || cbOp.SelectedIndex < 0) return; Overlay.Opac = Ops[cbOp.SelectedIndex]; Overlay.Commit(); };
            cbLay.SelectedIndexChanged += delegate { if (building || cbLay.SelectedIndex < 0) return; Overlay.Layout = cbLay.SelectedIndex; Overlay.Commit(); };
            cbMode.SelectedIndexChanged += delegate { if (building || cbMode.SelectedIndex < 0) return; Overlay.Mode = cbMode.SelectedIndex; Overlay.Commit(); };
            cbGrp.SelectedIndexChanged += delegate { if (!building) FillAvail(); };
            ContextMenuStrip cma = new ContextMenuStrip();
            cma.Items.Add(Lang.T("Adicionar ao overlay"), null, delegate { AddSel(); });
            cma.Items.Add(Lang.T("Adicionar todos os resultados da busca"), null, delegate { foreach (ListViewItem it in lvAv.Items) { string k = (string)it.Tag; if (!Overlay.Items.Contains(k)) Overlay.Items.Add(k); } Overlay.Commit(); });
            lvAv.ContextMenuStrip = cma;
            lvAv.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.A && e.Control) { foreach (ListViewItem it in lvAv.Items) it.Selected = true; e.Handled = true; } };
            chkCompact.CheckedChanged += delegate { if (building) return; Overlay.Compact = chkCompact.Checked; Overlay.Commit(); };
            chkColName.CheckedChanged += delegate { if (building) return; Overlay.ColName = chkColName.Checked; Overlay.Commit(); };
            cbHot.SelectedIndexChanged += delegate { if (building || cbHot.SelectedIndex < 0) return; Overlay.HotTemp = cbHot.SelectedIndex == 0 ? 0 : 70 + (cbHot.SelectedIndex - 1) * 5; Overlay.Commit(); };
            txSearch.Box.TextChanged += delegate { FillAvail(); };
            lvAv.ItemActivate += delegate { ToggleSel(); };
            lvAv.ItemDrag += delegate
            {
                List<string> ks = new List<string>(); foreach (ListViewItem it in lvAv.SelectedItems) ks.Add((string)it.Tag);
                if (ks.Count > 0) lvAv.DoDragDrop(new DataObject("buno.ovkey", string.Join("\n", ks.ToArray())), DragDropEffects.Copy);
            };
            lvAv.DragEnter += delegate(object o, DragEventArgs e) { e.Effect = e.Data.GetDataPresent("buno.ovkey") ? DragDropEffects.Move : DragDropEffects.None; };
            lvAv.DragDrop += delegate(object o, DragEventArgs e)
            {
                if (!e.Data.GetDataPresent("buno.ovkey")) return;
                foreach (string k in ((string)e.Data.GetData("buno.ovkey")).Split('\n')) Overlay.Items.Remove(k);   // soltar de volta na lista = remover
                Overlay.Commit();
            };
            lbSel.DrawItem += DrawSel;
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add(Lang.T("Cor..."), null, delegate { PickColor(); });
            cm.Items.Add(Lang.T("Renomear..."), null, delegate { RenameSel(); });
            cm.Items.Add(Lang.T("Aplicar esta cor a todos os itens"), null, delegate
            {
                int i = SelIdx(); Color c; if (i < 0 || !Overlay.Colors.TryGetValue(Overlay.Items[i], out c)) { Dlg.Show(this, "Defina antes uma cor para o item selecionado.", "Overlay em jogos"); return; }
                foreach (string k in Overlay.Items) Overlay.Colors[k] = c; Overlay.Commit();
            });
            cm.Items.Add(Lang.T("Resetar cor e nome"), null, delegate { ResetSel(); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(Lang.T("Subir"), null, delegate { MoveSel(-1); });
            cm.Items.Add(Lang.T("Descer"), null, delegate { MoveSel(1); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(Lang.T("Remover do overlay"), null, delegate { RemSel(); });
            lbSel.MouseDown += delegate(object o, MouseEventArgs e)
            {
                downIdx = lbSel.IndexFromPoint(e.Location); downPt = e.Location;
                if (e.Button == MouseButtons.Right && downIdx >= 0) { lbSel.SelectedIndex = downIdx; cm.Show(lbSel, e.Location); downIdx = -1; }
            };
            lbSel.MouseMove += delegate(object o, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || downIdx < 0 || downIdx >= Overlay.Items.Count) return;
                if (Math.Abs(e.X - downPt.X) + Math.Abs(e.Y - downPt.Y) < 6) return;
                int d = downIdx; downIdx = -1;
                lbSel.DoDragDrop(new DataObject("buno.ovkey", Overlay.Items[d]), DragDropEffects.Move | DragDropEffects.Copy);
                dropIdx = -1; lbSel.Invalidate();
            };
            lbSel.DragEnter += delegate(object o, DragEventArgs e) { e.Effect = e.Data.GetDataPresent("buno.ovkey") ? DragDropEffects.Move : DragDropEffects.None; };
            lbSel.DragOver += delegate(object o, DragEventArgs e)
            {
                if (!e.Data.GetDataPresent("buno.ovkey")) return;
                e.Effect = (e.AllowedEffect & DragDropEffects.Move) != 0 ? DragDropEffects.Move : DragDropEffects.Copy;
                dropIdx = DropIndex(lbSel.PointToClient(new Point(e.X, e.Y))); lbSel.Invalidate();
            };
            lbSel.DragLeave += delegate { dropIdx = -1; lbSel.Invalidate(); };
            lbSel.DragDrop += delegate(object o, DragEventArgs e)
            {
                if (!e.Data.GetDataPresent("buno.ovkey")) return;
                int to = DropIndex(lbSel.PointToClient(new Point(e.X, e.Y))); dropIdx = -1;
                foreach (string k in ((string)e.Data.GetData("buno.ovkey")).Split('\n'))
                {
                    if (k.Length == 0) continue;
                    int old = Overlay.Items.IndexOf(k);
                    if (old >= 0) { Overlay.Items.RemoveAt(old); if (old < to) to--; }
                    to = Math.Max(0, Math.Min(Overlay.Items.Count, to));
                    Overlay.Items.Insert(to, k); to++;
                }
                Overlay.Commit();
            };
            lbSel.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) { RemSel(); e.Handled = true; } };
            lbSel.DoubleClick += delegate { PickColor(); };

            Overlay.StateChanged += OnState;
            FormClosed += delegate { Overlay.StateChanged -= OnState; tm.Stop(); Overlay.PauseKey(false); if (Overlay.Editing) Overlay.SetEditing(false, null); };
            tm.Interval = 1000; tm.Tick += delegate { RefreshValues(); }; tm.Start();
            OnState();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (capturing)
            {
                Keys code = keyData & Keys.KeyCode;
                if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu) return true;   // so modificadores: espera a tecla
                capturing = false; Overlay.PauseKey(false);
                if (code == Keys.Escape) { }
                else if (code == Keys.Delete || code == Keys.Back) { Overlay.KeyVk = 0; Overlay.KeyMods = 0; }
                else
                {
                    Overlay.KeyVk = (int)code;
                    Overlay.KeyMods = ((keyData & Keys.Control) != 0 ? 1 : 0) | ((keyData & Keys.Shift) != 0 ? 2 : 0) | ((keyData & Keys.Alt) != 0 ? 4 : 0);
                }
                Overlay.Commit();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Z) || keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
            {
                if (!(ActiveControl is TextBoxBase) && !(ActiveControl is XpText))   // em caixas de texto, Ctrl+Z continua desfazendo o texto
                {
                    if (keyData == (Keys.Control | Keys.Z)) Overlay.Undo(); else Overlay.Redo();
                    return true;
                }
            }
            if (keyData == Keys.Escape)
            {
                ComboBox open = ActiveControl as ComboBox;
                if (open == null || !open.DroppedDown) { Close(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void DoExport()
        {
            using (SaveFileDialog sd = new SaveFileDialog())
            {
                sd.Filter = Lang.T("Configuração do overlay (*.bto)|*.bto|Todos os arquivos|*.*"); sd.FileName = "BunoTunner-overlay.bto"; sd.Title = Lang.T("Exportar configuração do overlay");
                if (sd.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(sd.FileName, Overlay.Export(), new UTF8Encoding(false)); Dlg.Show(this, "Configuração exportada.", "Overlay em jogos"); }
                catch (Exception ex) { Dlg.Show(this, "Não consegui salvar: " + ex.Message, "Overlay em jogos"); }
            }
        }
        void DoImport()
        {
            using (OpenFileDialog od = new OpenFileDialog())
            {
                od.Filter = Lang.T("Configuração do overlay (*.bto)|*.bto|Todos os arquivos|*.*"); od.Title = Lang.T("Importar configuração do overlay");
                if (od.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    FileInfo fi = new FileInfo(od.FileName);
                    if (fi.Length > 1024 * 1024) { Dlg.Show(this, "Arquivo grande demais para ser uma configuração do overlay.", "Overlay em jogos"); return; }
                    int n = Overlay.Import(File.ReadAllText(od.FileName));
                    if (n == 0) Dlg.Show(this, "Esse arquivo não tem uma configuração valida do overlay.", "Overlay em jogos");
                    else
                    {
                        building = true;
                        cbSize.SelectedIndex = Math.Max(0, Array.IndexOf(Sizes, Overlay.Scale)); cbOp.SelectedIndex = Math.Max(0, Array.IndexOf(Ops, Overlay.Opac)); cbMode.SelectedIndex = Overlay.Mode; cbLay.SelectedIndex = Overlay.Layout;
                        chkCompact.Checked = Overlay.Compact; chkColName.Checked = Overlay.ColName;
                        cbHot.SelectedIndex = Overlay.HotTemp <= 0 ? 0 : Math.Max(1, Math.Min(cbHot.Items.Count - 1, (Overlay.HotTemp - 70) / 5 + 1));
                        building = false;
                        btnKey.Text = "Mostrar/ocultar: " + Overlay.KeyName();
                        Dlg.Show(this, "Configuração importada. Use Ctrl+Z para voltar a anterior.", "Overlay em jogos");
                    }
                }
                catch (Exception ex) { Dlg.Show(this, "Não consegui ler o arquivo: " + ex.Message, "Overlay em jogos"); }
            }
        }

        // ---- ferramentas do item selecionado ----
        int SelIdx() { int i = lbSel.SelectedIndex; return i >= 0 && i < Overlay.Items.Count ? i : -1; }
        bool NeedSel() { if (SelIdx() >= 0) return true; Dlg.Show(this, "Selecione antes um item na lista da direita (Ordem no overlay).", "Overlay em jogos"); return false; }
        void PickColor()
        {
            if (!NeedSel()) return;
            string k = Overlay.Items[SelIdx()]; Color cur; if (!Overlay.Colors.TryGetValue(k, out cur)) cur = Color.FromArgb(240, 242, 246);
            using (ColorDialog cd = new ColorDialog())
            {
                cd.FullOpen = true; cd.AnyColor = true; cd.Color = cur;
                if (cd.ShowDialog(this) != DialogResult.OK) return;
                foreach (int i in lbSel.SelectedIndices) if (i < Overlay.Items.Count) Overlay.Colors[Overlay.Items[i]] = cd.Color;
                Overlay.Commit();
            }
        }
        void RenameSel()
        {
            if (!NeedSel()) return;
            string k = Overlay.Items[SelIdx()]; string cur; if (!Overlay.Names.TryGetValue(k, out cur)) cur = Overlay.LabelOf(k);
            string n = PromptForm.Ask(this, Lang.T("Renomear"), Lang.T("Nome que aparece no overlay (deixe vazio para voltar ao nome original):"), cur);
            if (n == null) return;
            Overlay.SetName(k, n == Overlay.LabelOf(k) ? "" : n);
        }
        void ResetSel()
        {
            if (!NeedSel()) return;
            foreach (int i in lbSel.SelectedIndices) if (i < Overlay.Items.Count) { Overlay.Colors.Remove(Overlay.Items[i]); Overlay.Names.Remove(Overlay.Items[i]); }
            Overlay.Commit();
        }
        void MoveSel(int d)
        {
            int i = SelIdx(); if (i < 0) { NeedSel(); return; }
            int j = i + d; if (j < 0 || j >= Overlay.Items.Count) return;
            Overlay.MoveItem(i, d < 0 ? j : j + 1);
            if (j < lbSel.Items.Count) lbSel.SelectedIndex = j;
        }

        static ComboBox Combo(Control p, int x, int y, int w)
        {
            ComboBox c = new ComboBox(); c.DropDownStyle = ComboBoxStyle.DropDownList; c.SetBounds(x, y, w, 24); c.Font = Xp.Ui; p.Controls.Add(c); return c;
        }
        int DropIndex(Point p)
        {
            int i = lbSel.IndexFromPoint(p);
            if (i < 0) return Overlay.Items.Count;
            Rectangle r = lbSel.GetItemRectangle(i);
            return p.Y > r.Top + r.Height / 2 ? i + 1 : i;
        }
        void OnState()
        {
            if (IsDisposed) return;
            btnEdit.Text = Overlay.Editing ? "Concluir edição" : "Editar na tela";
            if (!capturing) btnKey.Text = "Mostrar/ocultar: " + Overlay.KeyName() + (Overlay.KeyOk ? "" : " (em uso!)");
            building = true;
            int si = Array.IndexOf(Sizes, Overlay.Scale), oi = Array.IndexOf(Ops, Overlay.Opac);
            if (si >= 0) cbSize.SelectedIndex = si; if (oi >= 0) cbOp.SelectedIndex = oi;
            cbMode.SelectedIndex = Overlay.Mode; cbLay.SelectedIndex = Overlay.Layout; chkCompact.Checked = Overlay.Compact; chkColName.Checked = Overlay.ColName;
            cbHot.SelectedIndex = Overlay.HotTemp <= 0 ? 0 : Math.Max(1, Math.Min(cbHot.Items.Count - 1, (Overlay.HotTemp - 70) / 5 + 1));
            building = false;
            FillSel(); FillAvailMarks();
        }
        void FillSel()
        {
            int top = lbSel.TopIndex, selI = lbSel.SelectedIndex;
            lbSel.BeginUpdate(); lbSel.Items.Clear();
            foreach (string k in Overlay.Items) lbSel.Items.Add(k);
            lbSel.EndUpdate();
            if (top >= 0 && top < lbSel.Items.Count) lbSel.TopIndex = top;
            if (selI >= 0 && selI < lbSel.Items.Count) lbSel.SelectedIndex = selI;
        }
        void DrawSel(object o, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= lbSel.Items.Count) return;
            Graphics g = e.Graphics; bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(sel ? Xp.Sel : Xp.EditBg)) g.FillRectangle(b, e.Bounds);
            Color fg = sel ? Xp.Hi : Xp.Text;
            using (SolidBrush gb = new SolidBrush(sel ? Xp.Hi : Xp.Dim))
                for (int k = 0; k < 3; k++) g.FillRectangle(gb, e.Bounds.Left + 6, e.Bounds.Top + 7 + k * 4, 10, 2);
            string key = (string)lbSel.Items[e.Index];
            bool special = key.StartsWith("@");
            Sn s = special ? null : Overlay.Find(key);
            string val = special ? "" : (s != null ? Overlay.ValueOf(s) : "-");
            Color sw; bool has = Overlay.Colors.TryGetValue(key, out sw); if (!has) sw = Color.FromArgb(240, 242, 246);
            Rectangle swr = new Rectangle(e.Bounds.Left + 22, e.Bounds.Top + 6, 12, 12);
            using (SolidBrush sb = new SolidBrush(sw)) g.FillRectangle(sb, swr);
            using (Pen sp = new Pen(has ? fg : Xp.Dim)) g.DrawRectangle(sp, swr);
            string nm; string shown = Lang.T(Overlay.LabelOf(key)); if (Overlay.Names.TryGetValue(key, out nm)) shown = nm + "  (" + shown + ")";
            Rectangle tr = new Rectangle(e.Bounds.Left + 40, e.Bounds.Top, e.Bounds.Width - 116, e.Bounds.Height);
            TextRenderer.DrawText(g, shown, Xp.Ui, tr, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, val, Xp.UiBold, new Rectangle(e.Bounds.Right - 80, e.Bounds.Top, 74, e.Bounds.Height), sel ? Xp.Hi : Xp.Dim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (dropIdx >= 0 && (dropIdx == e.Index || (dropIdx == lbSel.Items.Count && e.Index == lbSel.Items.Count - 1)))
                using (Pen p = new Pen(Xp.Bad, 2f)) { int yy = dropIdx == e.Index ? e.Bounds.Top + 1 : e.Bounds.Bottom - 2; g.DrawLine(p, e.Bounds.Left, yy, e.Bounds.Right, yy); }
        }

        void FillAvail()
        {
            string q = txSearch.Text.Trim().ToLowerInvariant();
            string grp = cbGrp.SelectedIndex > 0 ? (string)cbGrp.SelectedItem : null;
            lvAv.BeginUpdate(); lvAv.Items.Clear();
            foreach (string[] sp in Overlay.Specials)
            {
                string sl = Lang.T(sp[1]);
                if (grp != null && grp != Lang.T("Jogo")) continue;
                if (q.Length > 0 && (sl + " fps api quadros").ToLowerInvariant().IndexOf(q) < 0) continue;
                ListViewItem f = new ListViewItem(sl); f.SubItems.Add(Lang.T("Jogo")); f.SubItems.Add(""); f.Tag = sp[0];
                lvAv.Items.Add(f);
            }
            foreach (Sn s in SensorHub.AllSensors.ToArray())
            {
                if (s.Type == "Temperature" && !s.IsRealTemp) continue;
                if (!s.Seen && !s.Last.HasValue) continue;
                if (grp != null && s.Area != grp) continue;
                string lab = Overlay.LabelOf(s);
                if (q.Length > 0 && (lab + " " + s.Group + " " + s.Type).ToLowerInvariant().IndexOf(q) < 0) continue;
                ListViewItem it = new ListViewItem(lab); it.SubItems.Add(s.Area); it.SubItems.Add(Overlay.ValueOf(s)); it.Tag = Overlay.OvKey(s);
                lvAv.Items.Add(it);
            }
            lvAv.EndUpdate();
            FillAvailMarks();
            if (cbGrp.Items.Count <= 1 || cbGrp.Tag == null || (int)cbGrp.Tag != SensorHub.AllSensors.Count)
            {
                List<string> areas = new List<string>(); areas.Add(Lang.T("Jogo"));
                foreach (Sn s in SensorHub.AllSensors.ToArray()) if (s.Area != null && !areas.Contains(s.Area)) areas.Add(s.Area);
                string cur = cbGrp.SelectedIndex > 0 ? (string)cbGrp.SelectedItem : null;
                building = true; cbGrp.Items.Clear(); cbGrp.Items.Add(Lang.T("Todos os grupos"));
                foreach (string a in areas) cbGrp.Items.Add(a);
                cbGrp.SelectedIndex = cur != null && cbGrp.Items.Contains(cur) ? cbGrp.Items.IndexOf(cur) : 0;
                cbGrp.Tag = SensorHub.AllSensors.Count; building = false;
            }
        }
        void FillAvailMarks()
        {
            foreach (ListViewItem it in lvAv.Items)
            {
                bool inUse = Overlay.Items.Contains((string)it.Tag);
                it.ForeColor = inUse ? Xp.Good : Xp.Text; it.Font = inUse ? Xp.UiBold : Xp.Ui;
            }
        }
        void RefreshValues()
        {
            lbSel.Invalidate();
            foreach (ListViewItem it in lvAv.Items)
            {
                string k = (string)it.Tag; if (k.StartsWith("@")) continue;
                Sn s = Overlay.Find(k); if (s != null && it.SubItems.Count > 2) it.SubItems[2].Text = Overlay.ValueOf(s);
            }
        }
        void AddSel()
        {
            foreach (ListViewItem it in lvAv.SelectedItems) { string k = (string)it.Tag; if (!Overlay.Items.Contains(k)) Overlay.Items.Add(k); }
            Overlay.Commit();
        }
        void ToggleSel()
        {
            foreach (ListViewItem it in lvAv.SelectedItems)
            {
                string k = (string)it.Tag;
                if (Overlay.Items.Contains(k)) Overlay.Items.Remove(k); else Overlay.Items.Add(k);
            }
            Overlay.Commit();
        }
        void RemSel()
        {
            List<string> rm = new List<string>();
            foreach (int i in lbSel.SelectedIndices) if (i < Overlay.Items.Count) rm.Add(Overlay.Items[i]);
            foreach (string k in rm) Overlay.Items.Remove(k);
            Overlay.Commit();
        }
    }

    class SensorForm : XpForm
    {
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        static readonly string[] Cats = { "Temperatura", "Uso (%)", "Clock", "Potência", "Voltagem", "Corrente", "Ventoinha", "Controle (%)", "Memória/Dados", "Taxa de dados", "Outros" };
        static readonly int[] Wins = { 60, 120, 300, 600, 1800 };
        static readonly Color[] Pal = {
            Color.FromArgb(0,200,0), Color.FromArgb(230,50,50), Color.FromArgb(50,130,255), Color.FromArgb(255,140,0),
            Color.FromArgb(190,70,210), Color.FromArgb(0,180,180), Color.FromArgb(240,60,150), Color.FromArgb(150,150,150),
            Color.FromArgb(120,200,60), Color.FromArgb(200,120,60), Color.FromArgb(90,90,230), Color.FromArgb(0,150,100) };

        string key; List<Sn> list; TileDef[] tiles; Sn[] tileSn; Label[] tileVal;
        XpText txSearch; ComboBox cbType, cbWin; XpOption chkHide, chkOnly, chkNorm;
        ListBox lbCat; Label lblHint; bool hintDriver, catUpdating;
        string[] catNames = { "Todos", "GPU", "CPU", "Placa-mae", "Memória RAM", "Discos", "Ventoinhas", "Coolers", "Rede", "Outros" };
        DateTime openedAt = DateTime.UtcNow;
        const string DefaultHint = "Dica: marque a caixinha (ou de duplo clique) para ver o sensor no gráfico. Botão direito = alerta, copiar... Clique no cabeçalho para ordenar.";
        ListView lv = new XpList(); XpGraph graph = new XpGraph(); Timer timer = new Timer();
        Label lblInfo; XpButton btnPause; ContextMenuStrip menu, exportMenu; LTip tip = new LTip();
        Dictionary<Sn, Series> ser = new Dictionary<Sn, Series>();
        Dictionary<Sn, ListViewItem> itemOf = new Dictionary<Sn, ListViewItem>();
        bool loading = true, sortAsc = true;
        int colorIdx, lastSeen = -1, sortCol = -1;
        DateTime infoUntil = DateTime.MinValue;
        XpText txInt; int lastPeriod;

        static TileDef[] AllTiles()
        {
            return new TileDef[] {
                new TileDef("GPU - temperatura", "@GPU", "Temperature", "GPU Core", "Temperatura do núcleo", "Core", "Hot Spot", ""),
                new TileDef("GPU - uso", "@GPU", "Load", "GPU Core", "Uso do núcleo", "Core", ""),
                new TileDef("GPU - consumo", "@GPU", "Power", "GPU Package", "Consumo (watts)", "Package", ""),
                new TileDef("CPU - temperatura", "Processador", "Temperature", "CPU Package", "Package", "Tdie", "CCD", "Core Max", "Core Average", "Tctl", "Core", ""),
                new TileDef("CPU - uso", "Processador", "Load", "CPU Total", ""),
                new TileDef("CPU - consumo", "Processador", "Power", "CPU Package", "Package", ""),
                new TileDef("RAM em uso", "Memória RAM", "Load", "Memory", ""),
                new TileDef("Ventoinha da CPU", "@PC", "Fan", "CPU Fan", "CPU", "Fan #1", "") };
        }
        static bool Match(Sn s, TileDef d)
        {
            if (s.Type != d.Type) return false;
            if (!(s.Seen || s.Last.HasValue)) return false;
            if (d.Type == "Temperature" && !s.IsRealTemp) return false;
            if (d.Group == "@GPU") return s.IsGpu;
            if (d.Group == "@PC") return !s.IsGpu;
            return d.Group == null || s.Group.StartsWith(d.Group);
        }
        static Sn Find(List<Sn> list, TileDef d)
        {
            foreach (string nm in d.Names)
            {
                foreach (Sn s in list)
                    if (Match(s, d) && string.Equals(s.Name, nm, StringComparison.OrdinalIgnoreCase)) return s;
                foreach (Sn s in list)
                    if (Match(s, d) && s.Name.IndexOf(nm, StringComparison.OrdinalIgnoreCase) >= 0) return s;
            }
            return null;
        }

        public SensorForm(string title, List<Sn> sensors)
        {
            key = "all"; list = sensors; Text = title;
            Size = new Size(1120, Math.Min(700, Screen.PrimaryScreen.WorkingArea.Height - 30));
            tiles = AllTiles();
            tileSn = new Sn[tiles.Length]; tileVal = new Label[tiles.Length];
            tip.InitialDelay = 300; tip.AutoPopDelay = 9000;
            KeyPreview = true;

            Panel right = new Panel(); right.Dock = DockStyle.Fill;
            Panel left = new Panel(); left.Dock = DockStyle.Left; left.Width = 650; left.Padding = new Padding(0, 0, 6, 0);
            Panel bottom = new Panel(); bottom.Dock = DockStyle.Bottom; bottom.Height = 36;
            Panel filters = new Panel(); filters.Dock = DockStyle.Top; filters.Height = 34;
            Panel top = new Panel(); top.Dock = DockStyle.Top; top.Height = 74;
            Controls.Add(right); Controls.Add(left); Controls.Add(bottom); Controls.Add(filters); Controls.Add(top);
            BuildTiles(top); BuildFilters(filters); BuildList(left); BuildGraphSide(right); BuildBottom(bottom); BuildMenus();

            // preferencias salvas
            chkHide.Checked = Settings.Get(key + ".hide", "1") == "1";
            chkNorm.Checked = Settings.Get(key + ".norm", "1") == "1"; graph.Normalize = chkNorm.Checked;
            int wi;
            if (!int.TryParse(Settings.Get(key + ".win", "1"), out wi) || wi < 0 || wi >= Wins.Length) wi = 1;
            cbWin.SelectedIndex = wi; ApplyWindow();
            string saved = Settings.Get(key + ".plot", null);
            if (saved == null) Essentials();
            else
                foreach (string k in saved.Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries))
                    foreach (Sn s in list)
                        if (s.Key == k) { Plot(s, true); break; }

            EventHandler rb = delegate { if (!catUpdating) Rebuild(); };
            txSearch.Box.TextChanged += rb; lbCat.SelectedIndexChanged += rb; cbType.SelectedIndexChanged += rb;
            chkHide.CheckedChanged += rb; chkOnly.CheckedChanged += rb;
            cbWin.SelectedIndexChanged += delegate { ApplyWindow(); };
            chkNorm.CheckedChanged += delegate { graph.Normalize = chkNorm.Checked; graph.Invalidate(); };
            lv.ItemChecked += OnChecked;
            lv.ItemActivate += delegate
            {
                foreach (ListViewItem it in lv.SelectedItems) { Sn sn = (Sn)it.Tag; Plot(sn, !ser.ContainsKey(sn)); }
            };
            lv.ColumnClick += delegate(object o, ColumnClickEventArgs e)
            {
                if (sortCol == e.Column) sortAsc = !sortAsc; else { sortCol = e.Column; sortAsc = true; }
                Rebuild();
            };

            KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.F) { txSearch.Box.Focus(); txSearch.Box.SelectAll(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape && txSearch.Box.Focused) { txSearch.Text = ""; e.SuppressKeyPress = true; }
            };
            Rebuild();
            timer.Interval = Math.Max(100, SensorHub.IntervalMs); lastPeriod = SensorHub.IntervalMs;
            timer.Tick += delegate { try { Tick2(); } catch (Exception) { } };
            timer.Start();
            FormClosing += delegate { SaveSettings(); };
            Action langCh = delegate { RefreshCats(); };
            Lang.Changed += langCh;
            FormClosed += delegate { timer.Stop(); Lang.Changed -= langCh; };
            Tick2();
        }

        // ---------- construcao da tela ----------
        void BuildTiles(Panel top)
        {
            XpGroup[] tg = new XpGroup[tiles.Length];
            for (int i = 0; i < tiles.Length; i++)
            {
                XpGroup g = new XpGroup(); g.Title = tiles[i].Title; g.Padding = new Padding(4, 20, 4, 2);
                Label v = new Label(); v.Dock = DockStyle.Fill; v.BackColor = Color.Transparent; v.Font = Xp.Big;
                v.ForeColor = Xp.GroupText; v.TextAlign = ContentAlignment.MiddleCenter; v.Text = "-";
                g.Controls.Add(v); top.Controls.Add(g);
                tg[i] = g; tileVal[i] = v;
            }
            EventHandler place = delegate
            {
                int w = (top.Width - 6 * (tg.Length - 1)) / tg.Length;
                for (int j = 0; j < tg.Length; j++) tg[j].SetBounds(j * (w + 6), 4, w, 66);
            };
            top.Resize += place; place(null, EventArgs.Empty);
        }

        void BuildFilters(Panel f)
        {
            UI.Lbl(f, "Buscar (Ctrl+F):", 4, 10, Xp.UiBold);
            txSearch = UI.Txt(f, 104, 6, 190, "");
            UI.Btn(f, "X", 298, 5, 24, delegate { txSearch.Text = ""; txSearch.Box.Focus(); });
            UI.Lbl(f, "Tipo:", 334, 10, Xp.UiBold);
            cbType = new ComboBox(); cbType.DropDownStyle = ComboBoxStyle.DropDownList; cbType.Font = Xp.Ui;
            cbType.SetBounds(368, 6, 120, 22); f.Controls.Add(cbType);
            cbType.Items.Add("Todos os tipos");
            foreach (string c in Cats) cbType.Items.Add(c);
            cbType.SelectedIndex = 0;
            chkHide = UI.Opt(f, "Ocultar sem leitura", 500, 8, 140, false);
            chkOnly = UI.Opt(f, "Só no gráfico", 646, 8, 100, false);
            UI.Btn(f, "Essenciais", 756, 5, 84, delegate { Essentials(); });
            UI.Btn(f, "Desmarcar tudo", 846, 5, 110, delegate { ClearPlot(); });
            tip.SetToolTip(txSearch, "Digite parte do nome. Várias palavras = todas precisam aparecer (ex.: core temp). Ctrl+A seleciona tudo.");
            tip.SetToolTip(txSearch.Box, "Digite parte do nome. Várias palavras = todas precisam aparecer (ex.: core temp). Ctrl+A seleciona tudo.");
            tip.SetToolTip(cbType, "Filtrar por tipo de medida (temperatura, uso, clock...)");
        }

        void BuildList(Panel left)
        {
            lv.Dock = DockStyle.Fill; lv.View = View.Details; lv.CheckBoxes = true; lv.FullRowSelect = true; lv.GridLines = true;
            lv.BackColor = Xp.EditBg; lv.Font = Xp.Ui; lv.HideSelection = false; lv.MultiSelect = true;
            lv.Columns.Add("Sensor", 200); lv.Columns.Add("Valor", 85); lv.Columns.Add("Min", 70); lv.Columns.Add("Max", 70); lv.Columns.Add("Tipo", 70);
            UI.DoubleBuffer(lv);
            lbCat = new ListBox(); lbCat.Dock = DockStyle.Left; lbCat.Width = 140; lbCat.Font = Xp.Ui; lbCat.IntegralHeight = false;
            lbCat.BorderStyle = BorderStyle.FixedSingle;
            foreach (string c in catNames) lbCat.Items.Add(Lang.T(c));
            lbCat.SelectedIndex = 0;
            lblHint = new Label(); lblHint.Dock = DockStyle.Top; lblHint.Height = 34; lblHint.Font = Xp.Ui; lblHint.ForeColor = Xp.Dim;
            lblHint.Text = DefaultHint;
            lblHint.Click += delegate { if (hintDriver) DriverHelp(); };
            left.Controls.Add(lv); left.Controls.Add(lbCat); left.Controls.Add(lblHint);
            tip.SetToolTip(lbCat, "Escolha o que você quer ver: GPU, CPU, placa-mae, memória, discos, ventoinhas...");
        }
        void BuildGraphSide(Panel right)
        {
            Panel gt = new Panel(); gt.Dock = DockStyle.Top; gt.Height = 30;
            UI.Lbl(gt, "Janela:", 4, 9, Xp.UiBold);
            cbWin = new ComboBox(); cbWin.DropDownStyle = ComboBoxStyle.DropDownList; cbWin.Font = Xp.Ui; cbWin.SetBounds(52, 5, 70, 22);
            gt.Controls.Add(cbWin);
            cbWin.Items.AddRange(new object[] { "1 min", "2 min", "5 min", "10 min", "30 min" });
            cbWin.SelectedIndex = 1;
            chkNorm = UI.Opt(gt, "Normalizar escala", 128, 6, 124, false); chkNorm.Checked = true;
            btnPause = UI.Btn(gt, "Pausar", 256, 3, 66, null);
            btnPause.Click += delegate { graph.Paused = !graph.Paused; btnPause.Text = graph.Paused ? "Continuar" : "Pausar"; graph.Invalidate(); };
            UI.Btn(gt, "Limpar histórico", 326, 3, 112, delegate { foreach (Series s in graph.Items) s.V.Clear(); graph.Invalidate(); });
            tip.SetToolTip(chkNorm, "Ligado: cada linha usa a própria escala (0-100%). Desligado: mesma escala para todas.");
            tip.SetToolTip(graph, "Passe o mouse sobre o gráfico para ver os valores de cada instante");
            graph.Dock = DockStyle.Fill;
            right.Controls.Add(graph); right.Controls.Add(gt);
        }

        void BuildBottom(Panel b)
        {
            XpButton ex = UI.Btn(b, "Exportar...", 4, 6, 100, null);
            ex.Click += delegate { exportMenu.Show(ex, new Point(0, ex.Height)); };
            UI.Btn(b, "Zerar min/max", 112, 6, 110, delegate { foreach (Sn s in list) s.ResetMinMax(); });
            UI.Lbl(b, "Atualizar a cada:", 232, 12, Xp.UiBold);
            txInt = UI.Txt(b, 332, 8, 52, SensorHub.IntervalMs.ToString());
            UI.Lbl(b, "ms", 388, 12);
            UI.Btn(b, "OK", 412, 6, 40, delegate { CommitInterval(); });
            txInt.Box.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitInterval(); } };
            tip.SetToolTip(txInt, "Tempo entre as leituras dos sensores (100 a 10000 ms). Menos = mais fluido, mas usa mais CPU.");
            UI.Btn(b, "Overlay em jogos...", 466, 6, 150, delegate { OverlayConfigForm.Open(this); });
            lblInfo = UI.Lbl(b, "", 626, 12, Xp.Ui, Xp.Dim);
            XpButton close = UI.Btn(b, "Fechar", 0, 6, 90, delegate { Close(); });
            close.Left = b.Width - 96;
            b.Resize += delegate { close.Left = b.Width - 96; };
        }

        void BuildMenus()
        {
            menu = new ContextMenuStrip(); menu.RenderMode = ToolStripRenderMode.System;
            menu.Items.Add("Mostrar / ocultar no gráfico", null, delegate { TogglePlot(); });
            menu.Items.Add("Definir alerta (acima de...)", null, delegate { SetAlertDlg(); });
            menu.Items.Add("Remover alerta", null, delegate { RemoveAlert(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Copiar nome", null, delegate { CopySel(false); });
            menu.Items.Add("Copiar valor", null, delegate { CopySel(true); });
            menu.Items.Add("Zerar min/max deste sensor", null, delegate { foreach (ListViewItem it in lv.SelectedItems) ((Sn)it.Tag).ResetMinMax(); });
            lv.ContextMenuStrip = menu; Lang.Menu(menu);

            exportMenu = new ContextMenuStrip(); exportMenu.RenderMode = ToolStripRenderMode.System;
            exportMenu.Items.Add("Valores atuais (CSV)...", null, delegate { ExportValues(); });
            exportMenu.Items.Add("Histórico do gráfico (CSV)...", null, delegate { ExportHistory(); });
            exportMenu.Items.Add("Imagem do gráfico (PNG)...", null, delegate { ExportImage(); });
            Lang.Menu(exportMenu);
        }

        // ---------- logica ----------
        void RefreshCats()
        {
            int[] n = new int[catNames.Length];
            foreach (Sn s in list)
            {
                if (chkHide.Checked && !s.Seen) continue;
                n[0]++;
                for (int i = 1; i < catNames.Length; i++) if (s.InArea(catNames[i])) n[i]++;
            }
            catUpdating = true;
            int sel = lbCat.SelectedIndex;
            for (int i = 0; i < catNames.Length; i++)
            {
                string t = Lang.T(catNames[i]) + "  (" + n[i] + ")";
                if ((string)lbCat.Items[i] != t) lbCat.Items[i] = t;
            }
            if (lbCat.SelectedIndex != sel) lbCat.SelectedIndex = sel;
            catUpdating = false;
        }

        void ShowHelp()
        {
            Dlg.Show(
                "COMO USAR OS SENSORES\n\n" +
                "- Cartões no topo: os valores principais (GPU, CPU, RAM, ventoinha) sempre a vista.\n" +
                "- Lista: escolha a categoria a esquerda (GPU, CPU, Placa-mae, Memória...) e use a busca (Ctrl+F) e o filtro de tipo.\n" +
                "- Marque a caixinha (ou de duplo clique) para ver o sensor no gráfico.\n" +
                "- Botão direito: criar ALERTA (som), copiar nome/valor, zerar min/max.\n" +
                "- \"Essenciais\" marca os sensores mais importantes de uma vez.\n" +
                "- Gráfico: passe o mouse para ver os valores; \"Normalizar escala\" coloca unidades diferentes lado a lado.\n" +
                "- \"Atualizar a cada\" muda a velocidade das leituras (em milissegundos).\n" +
                "- Exportar salva os valores/histórico em CSV ou o gráfico em PNG.",
                "BunoTunner - Sensores", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void DriverHelp()
        {
            if (Dlg.Show(
                "Os sensores da CPU (temperatura, tensão, consumo) precisam do driver do LibreHardwareMonitor, e ele não está conseguindo ler esta CPU.\n\n" +
                "Causas comuns:\n" +
                "1) \"Integridade de memória\" (Isolamento de núcleo) do Windows ou o Defender bloqueando o driver: desative em Segurança do Windows > Segurança do dispositivo > Isolamento de núcleo e reinicie o PC.\n" +
                "2) Versão do LibreHardwareMonitor antiga para a sua CPU: use a mais recente (versões novas podem exigir o driver PawnIO).\n" +
                "3) Antivírus de terceiros.\n\n" +
                "Enquanto isso o programa usa as zonas térmicas do Windows (menos precisas).\n\nAbrir agora a tela de Isolamento de núcleo?",
                "Sensores da CPU sem leitura", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { Process.Start("windowsdefender://coreisolation"); } catch (Exception) { }
        }

        void ApplyWindow()
        {
            int period = Math.Max(100, SensorHub.IntervalMs);
            graph.PeriodMs = period;
            graph.Window = Math.Min(XpGraph.Store, Math.Max(10, Wins[cbWin.SelectedIndex] * 1000 / period));
        }

        void CommitInterval()
        {
            SensorHub.SetInterval(UI.Int(txInt, SensorHub.IntervalMs));
            txInt.Text = SensorHub.IntervalMs.ToString();
            Say("Sensores atualizando a cada " + SensorHub.IntervalMs + " ms.");
        }

        void Say(string msg) { lblInfo.Text = msg; infoUntil = DateTime.UtcNow.AddSeconds(6); }

        void Plot(Sn s, bool on)
        {
            ListViewItem it; itemOf.TryGetValue(s, out it);
            Series se;
            if (on)
            {
                if (ser.ContainsKey(s)) return;
                se = new Series(); se.Sn = s; se.Color = Pal[colorIdx++ % Pal.Length];
                ser[s] = se; graph.Items.Add(se);
                if (it != null) { loading = true; it.Checked = true; it.ForeColor = se.Color; loading = false; }
            }
            else
            {
                if (!ser.TryGetValue(s, out se)) return;
                graph.Items.Remove(se); ser.Remove(s);
                if (it != null) { loading = true; it.Checked = false; it.ForeColor = Xp.Text; loading = false; }
            }
            graph.Invalidate();
        }

        void OnChecked(object o, ItemCheckedEventArgs e)
        {
            if (loading) return;
            Sn sn = (Sn)e.Item.Tag;
            Plot(sn, e.Item.Checked);
            if (chkOnly.Checked && !e.Item.Checked) BeginInvoke(new Action(Rebuild));
        }

        void Essentials()
        {
            foreach (TileDef t in tiles) { Sn s = Find(list, t); if (s != null) Plot(s, true); }
            if (chkOnly != null && chkOnly.Checked) Rebuild();
        }

        void ClearPlot()
        {
            foreach (Sn s in new List<Sn>(ser.Keys)) Plot(s, false);
            if (chkOnly.Checked) Rebuild();
        }

        void Rebuild()
        {
            string q = txSearch.Text.Trim().ToLower();
            string[] words = q.Length == 0 ? new string[0] : q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string area = (lbCat.SelectedIndex > 0 && lbCat.SelectedIndex < catNames.Length) ? catNames[lbCat.SelectedIndex] : null;
            string cat = cbType.SelectedIndex > 0 && cbType.SelectedIndex <= Cats.Length ? Cats[cbType.SelectedIndex - 1] : null;
            loading = true;
            lv.BeginUpdate();
            lv.Items.Clear(); lv.Groups.Clear(); itemOf.Clear();
            lv.ShowGroups = true;
            Dictionary<string, ListViewGroup> groups = new Dictionary<string, ListViewGroup>();
            int shown = 0;
            foreach (Sn s in list)
            {
                if (area != null && !s.InArea(area)) continue;
                if (cat != null && s.Category != cat) continue;
                if (chkHide.Checked && !s.Seen) continue;
                if (chkOnly.Checked && !ser.ContainsKey(s)) continue;
                if (words.Length > 0)
                {
                    string hay = (s.Name + " " + s.Group).ToLower();
                    bool ok = true;
                    foreach (string w in words) if (hay.IndexOf(w) < 0) { ok = false; break; }
                    if (!ok) continue;
                }
                ListViewItem it = new ListViewItem(new string[] { s.Name, s.Fmt(s.Last), s.Seen ? s.Fmt(s.Min) : "-", s.Seen ? s.Fmt(s.Max) : "-", s.Category });
                it.Tag = s;
                if (true)
                {
                    ListViewGroup g;
                    if (!groups.TryGetValue(s.Group, out g)) { g = new ListViewGroup(s.Group); groups[s.Group] = g; lv.Groups.Add(g); }
                    it.Group = g;
                }
                Series se;
                if (ser.TryGetValue(s, out se)) { it.Checked = true; it.ForeColor = se.Color; }
                lv.Items.Add(it); itemOf[s] = it; shown++;
            }
            if (sortCol >= 0) { lv.ListViewItemSorter = new SnSorter(sortCol, sortAsc); lv.Sort(); }
            else lv.ListViewItemSorter = null;
            lv.EndUpdate();
            loading = false;
            RefreshCats();
            if (lblInfo != null && DateTime.UtcNow > infoUntil) InfoText(shown);
        }

        void InfoText(int shown)
        {
            int alerts = 0;
            foreach (Sn s in list) if (s.AlertAbove.HasValue) alerts++;
            lblInfo.Text = shown + Lang.Pick(" de ", " of ") + list.Count + " sensores  |  " + ser.Count + " no gráfico  |  " + alerts + " alerta(s)";
        }

        static void SetSub(ListViewItem it, int i, string t) { t = Lang.T(t); if (it.SubItems[i].Text != t) it.SubItems[i].Text = t; }

        void Tick2()
        {
            if (lastPeriod != SensorHub.IntervalMs)
            {
                lastPeriod = SensorHub.IntervalMs;
                timer.Interval = Math.Max(100, lastPeriod);
                ApplyWindow();
            }
            int seen = 0;
            foreach (Sn s in list) if (s.Seen) seen++;
            if (seen != lastSeen) { lastSeen = seen; if (chkHide.Checked) Rebuild(); else RefreshCats(); }
            bool needHint = !SensorHub.CpuDriverOk() && (DateTime.UtcNow - openedAt).TotalSeconds > 6;
            if (needHint != hintDriver)
            {
                hintDriver = needHint;
                lblHint.ForeColor = needHint ? Xp.Bad : Xp.Dim;
                lblHint.Cursor = needHint ? Cursors.Hand : Cursors.Default;
                lblHint.Text = needHint ? "(!) Os sensores da CPU não estão sendo lidos pelo driver (temperatura/tensão/consumo). Clique aqui para ver como resolver." : DefaultHint;
            }

            for (int i = 0; i < tiles.Length; i++)
            {
                if (tileSn[i] == null) tileSn[i] = Find(list, tiles[i]);
                Sn t = tileSn[i];
                tileVal[i].Text = (t != null && t.Last.HasValue) ? t.Fmt(t.Last) : "-";
                Color c = Xp.GroupText;
                if (t != null && t.Last.HasValue && t.Type == "Temperature")
                    c = t.Last.Value >= 85 ? Xp.Bad : (t.Last.Value >= 70 ? Color.DarkOrange : Xp.GroupText);
                if (tileVal[i].ForeColor != c) tileVal[i].ForeColor = c;
            }

            lv.BeginUpdate();
            foreach (ListViewItem it in lv.Items)
            {
                Sn s = (Sn)it.Tag;
                SetSub(it, 1, s.Fmt(s.Last));
                SetSub(it, 2, s.Seen ? s.Fmt(s.Min) : "-");
                SetSub(it, 3, s.Seen ? s.Fmt(s.Max) : "-");
                bool al = s.AlertAbove.HasValue;
                bool hit = al && s.Last.HasValue && s.Last.Value > s.AlertAbove.Value;
                string nm = (al ? "(!) " : "") + s.Name;
                if (it.Text != nm) it.Text = nm;
                Color bg = hit ? Xp.HitBg : Xp.EditBg;
                if (it.BackColor != bg) it.BackColor = bg;
            }
            lv.EndUpdate();

            if (!graph.Paused)
                foreach (Series se in graph.Items)
                {
                    if (!se.Sn.Last.HasValue) continue;
                    se.V.Add(se.Sn.Last.Value);
                    if (se.V.Count > XpGraph.Store) se.V.RemoveAt(0);
                }
            graph.Invalidate();
            if (DateTime.UtcNow > infoUntil) InfoText(lv.Items.Count);
        }

        // ---------- menu de contexto ----------
        void TogglePlot()
        {
            List<Sn> sel = new List<Sn>();
            foreach (ListViewItem it in lv.SelectedItems) sel.Add((Sn)it.Tag);
            bool anyOff = false;
            foreach (Sn s in sel) if (!ser.ContainsKey(s)) anyOff = true;
            foreach (Sn s in sel) Plot(s, anyOff);
        }

        void SetAlertDlg()
        {
            if (lv.SelectedItems.Count == 0) return;
            Sn s = (Sn)lv.SelectedItems[0].Tag;
            string cur = s.AlertAbove.HasValue ? s.AlertAbove.Value.ToString("0.##") : "";
            string r = PromptForm.Ask(this, "Alerta", "Avisar (som + barra de status) quando \"" + s.Name + "\" passar de (" + Sn.Unit(s.Type) + "):", cur);
            if (r == null || r.Length == 0) return;
            float v;
            if (!float.TryParse(r.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { Say("Valor inválido."); return; }
            SensorHub.SetAlert(s, v);
            Say("Alerta definido: " + s.Name + " > " + v.ToString("0.##") + " " + Sn.Unit(s.Type));
        }

        void RemoveAlert()
        {
            foreach (ListViewItem it in lv.SelectedItems) SensorHub.SetAlert((Sn)it.Tag, null);
            Say("Alerta removido.");
        }

        void CopySel(bool value)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ListViewItem it in lv.SelectedItems)
            {
                Sn s = (Sn)it.Tag;
                sb.AppendLine(value ? s.Name + ": " + s.Fmt(s.Last) : s.Name);
            }
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); Say("Copiado."); } catch (Exception) { }
        }

        // ---------- exportar ----------
        static string Csv(string t) { return (t.IndexOf(';') >= 0 || t.IndexOf('"') >= 0) ? "\"" + t.Replace("\"", "\"\"") + "\"" : t; }

        string AskFile(string filter, string name)
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = Lang.T(filter); d.FileName = name + "_" + key + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
            }
        }

        void ExportValues()
        {
            string f = AskFile("CSV (*.csv)|*.csv", "sensores");
            if (f == null) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(Lang.T("Grupo;Sensor;Tipo;Valor;Mínimo;Máximo;Unidade"));
                foreach (Sn s in list)
                    if (s.Seen)
                        sb.AppendLine(Csv(s.Group) + ";" + Csv(s.Name) + ";" + s.Category + ";" + (s.Last.HasValue ? s.Last.Value.ToString("0.##") : "") + ";" +
                                      s.Min.ToString("0.##") + ";" + s.Max.ToString("0.##") + ";" + Sn.Unit(s.Type));
                File.WriteAllText(f, sb.ToString(), new UTF8Encoding(true));
                Say("Salvo: " + f);
            }
            catch (Exception ex) { Dlg.Show(ex.Message, "Exportar"); }
        }

        void ExportHistory()
        {
            if (graph.Items.Count == 0) { Say("Marque sensores para o gráfico primeiro."); return; }
            string f = AskFile("CSV (*.csv)|*.csv", "histórico");
            if (f == null) return;
            try
            {
                int max = 0;
                foreach (Series s in graph.Items) max = Math.Max(max, s.V.Count);
                StringBuilder sb = new StringBuilder("Segundos");
                foreach (Series s in graph.Items) sb.Append(";" + Csv(s.Sn.Name + " (" + Sn.Unit(s.Sn.Type) + ")"));
                sb.AppendLine();
                for (int i = 0; i < max; i++)
                {
                    sb.Append(-(max - 1 - i));
                    foreach (Series s in graph.Items)
                    {
                        int idx = s.V.Count - (max - i);
                        sb.Append(";" + (idx >= 0 ? s.V[idx].ToString("0.##") : ""));
                    }
                    sb.AppendLine();
                }
                File.WriteAllText(f, sb.ToString(), new UTF8Encoding(true));
                Say("Salvo: " + f);
            }
            catch (Exception ex) { Dlg.Show(ex.Message, "Exportar"); }
        }

        void ExportImage()
        {
            string f = AskFile("PNG (*.png)|*.png", "gráfico");
            if (f == null) return;
            try
            {
                using (Bitmap bmp = new Bitmap(graph.Width, graph.Height))
                {
                    graph.DrawToBitmap(bmp, new Rectangle(0, 0, graph.Width, graph.Height));
                    bmp.Save(f, ImageFormat.Png);
                }
                Say("Salvo: " + f);
            }
            catch (Exception ex) { Dlg.Show(ex.Message, "Exportar"); }
        }

        void SaveSettings()
        {
            List<string> ks = new List<string>();
            foreach (Sn s in ser.Keys) ks.Add(s.Key);
            Settings.Set(key + ".plot", string.Join("~|~", ks.ToArray()));
            Settings.Set(key + ".win", cbWin.SelectedIndex.ToString());
            Settings.Set(key + ".norm", chkNorm.Checked ? "1" : "0");
            Settings.Set(key + ".hide", chkHide.Checked ? "1" : "0");
        }
    }

    // ------------------------------------------------------------------
    //  Recursos do sistema / firmware (ligado ou desligado)
    // ------------------------------------------------------------------
    static class Platform
    {
        [DllImport("kernel32.dll")] static extern bool GetFirmwareType(out int t);
        [DllImport("kernel32.dll")] static extern bool IsProcessorFeaturePresent(uint f);
        public const string On = "Ligado", Off = "Desligado", NA = "Indisponível";

        static object Reg(RegistryKey root, string sub, string name)
        {
            try { using (RegistryKey k = root.OpenSubKey(sub)) { if (k != null) return k.GetValue(name); } } catch (Exception) { }
            return null;
        }
        static string Flag(object v, int onValue, string naText)
        {
            if (v == null) return naText;
            try { return Convert.ToInt32(v) == onValue ? On : Off; } catch (Exception) { return naText; }
        }
        static string Feature(uint f)
        {
            try { return IsProcessorFeaturePresent(f) ? On : Off; } catch (Exception) { return NA; }
        }

        public static string UefiText()
        {
            try
            {
                int t;
                if (GetFirmwareType(out t)) { if (t == 2) return On + " (UEFI)"; if (t == 1) return Off + " (BIOS legado / CSM)"; }
            }
            catch (Exception) { }
            return NA;
        }
        public static string SecureBoot() { return Flag(Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled"), 1, NA + " (BIOS legado ou sem suporte)"); }
        public static string Hags() { return Flag(Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"), 2, NA + " (Windows/driver sem suporte)"); }
        public static string GameMode()
        {
            object v = Reg(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
            return v == null ? "Padrão do Windows (não alterado)" : Flag(v, 1, NA);
        }
        static string Hvci()
        {
            object v = Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
            return v == null ? Off : Flag(v, 1, NA);
        }
        static string FastStartup() { return Flag(Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled"), 1, NA); }
        static string OsName()
        {
            string k = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            string n = Convert.ToString(Reg(Registry.LocalMachine, k, "ProductName"));
            string dv = Convert.ToString(Reg(Registry.LocalMachine, k, "DisplayVersion"));
            string b = Convert.ToString(Reg(Registry.LocalMachine, k, "CurrentBuild"));
            return (n + " " + dv + " (build " + b + ")").Trim();
        }

        // leituras rapidas (registro / API)
        public static List<string[]> Fast()
        {
            List<string[]> l = new List<string[]>();
            l.Add(new string[] { "Sistema operacional", OsName() });
            l.Add(new string[] { "Boot em modo UEFI", UefiText() });
            l.Add(new string[] { "Secure Boot", SecureBoot() });
            l.Add(new string[] { "Virtualização no firmware (VT-x / AMD-V)", Feature(21) });
            l.Add(new string[] { "SLAT (paginação aninhada)", Feature(20) });
            l.Add(new string[] { "DEP / NX (proteção de execução)", Feature(12) });
            l.Add(new string[] { "Integridade de memória (HVCI)", Hvci() });
            l.Add(new string[] { "Inicialização rápida (Fast Startup)", FastStartup() });
            return l;
        }

        static object Wmi(string scope, string query, string prop)
        {
            try
            {
                ManagementScope sc = new ManagementScope(scope);
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(sc, new ObjectQuery(query)))
                using (ManagementObjectCollection col = s.Get())
                    foreach (ManagementBaseObject mo in col) { object v = mo[prop]; mo.Dispose(); return v; }
            }
            catch (Exception) { }
            return null;
        }

        // leituras via WMI (lentas - rodar em segundo plano)
        public static List<string[]> Slow()
        {
            List<string[]> l = new List<string[]>();
            string tscope = @"\\.\root\CIMV2\Security\MicrosoftTpm";
            object en = Wmi(tscope, "SELECT * FROM Win32_Tpm", "IsEnabled_InitialValue");
            object ver = Wmi(tscope, "SELECT * FROM Win32_Tpm", "SpecVersion");
            string tpm;
            if (en == null) tpm = NA + " (sem TPM ou desativado no firmware)";
            else
            {
                tpm = Convert.ToBoolean(en) ? On : Off;
                if (ver != null) tpm += " (v" + ver.ToString().Split(',')[0].Trim() + ")";
            }
            l.Add(new string[] { "TPM", tpm });

            object vbs = Wmi(@"\\.\root\Microsoft\Windows\DeviceGuard", "SELECT * FROM Win32_DeviceGuard", "VirtualizationBasedSecurityStatus");
            string vb = NA;
            if (vbs != null)
            {
                int n = Convert.ToInt32(vbs);
                vb = n == 2 ? On + " (em execução)" : (n == 1 ? On + " (aguardando reinicio)" : Off);
            }
            l.Add(new string[] { "Segurança baseada em virtualização (VBS)", vb });

            object hv = Wmi(@"\\.\root\CIMV2", "SELECT * FROM Win32_ComputerSystem", "HypervisorPresent");
            l.Add(new string[] { "Hypervisor ativo (Hyper-V / WSL2)", hv == null ? NA : (Convert.ToBoolean(hv) ? On : Off) });
            return l;
        }
    }

    // ------------------------------------------------------------------
    //  Dados extras da GPU via WMI (driver do Windows, resolucao...)
    // ------------------------------------------------------------------
    static class GpuWmi
    {
        static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0 || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public static List<string[]> Collect(string gpuName)
        {
            List<string[]> l = new List<string[]>();
            Action<string, string> add = delegate(string k, string v) { if (!string.IsNullOrEmpty(v)) l.Add(new string[] { k, v }); };
            try
            {
                using (ManagementObjectSearcher srch = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                using (ManagementObjectCollection col = srch.Get())
                    foreach (ManagementBaseObject mo in col)
                    {
                        if (!Same(CpuInfo.P(mo, "Name"), gpuName)) { mo.Dispose(); continue; }
                        add("Versão do driver (Windows)", CpuInfo.P(mo, "DriverVersion"));
                        string dd = CpuInfo.P(mo, "DriverDate");
                        if (!string.IsNullOrEmpty(dd))
                        {
                            try { add("Data do driver", ManagementDateTimeConverter.ToDateTime(dd).ToString("dd/MM/yyyy")); } catch (Exception) { }
                        }
                        string hr = CpuInfo.P(mo, "CurrentHorizontalResolution"), vr = CpuInfo.P(mo, "CurrentVerticalResolution");
                        if (hr != null && vr != null) add("Resolução atual", hr + " x " + vr);
                        string rf = CpuInfo.P(mo, "CurrentRefreshRate");
                        if (rf != null) add("Taxa de atualização do monitor", rf + " Hz");
                        add("Processador de video", CpuInfo.P(mo, "VideoProcessor"));
                        add("ID de hardware (PNP)", CpuInfo.P(mo, "PNPDeviceID"));
                        mo.Dispose();
                        break;
                    }
            }
            catch (Exception) { }
            return l;
        }
    }

    // ------------------------------------------------------------------
    //  OpenCL (via OpenCL.dll que ja vem com o driver da GPU) - estresse de GPU
    // ------------------------------------------------------------------
    static class Cl
    {
        const string DLL = "OpenCL.dll";
        [DllImport(DLL)] public static extern int clGetPlatformIDs(uint n, IntPtr[] plats, out uint cnt);
        [DllImport(DLL)] public static extern int clGetDeviceIDs(IntPtr plat, ulong type, uint n, IntPtr[] devs, out uint cnt);
        [DllImport(DLL, EntryPoint = "clGetDeviceInfo")] public static extern int GetDevStr(IntPtr dev, uint param, IntPtr size, StringBuilder val, out IntPtr ret);
        [DllImport(DLL, EntryPoint = "clGetDeviceInfo")] public static extern int GetDevU64(IntPtr dev, uint param, IntPtr size, out ulong val, out IntPtr ret);
        [DllImport(DLL)] public static extern IntPtr clCreateContext(IntPtr props, uint n, IntPtr[] devs, IntPtr notify, IntPtr user, out int err);
        [DllImport(DLL)] public static extern IntPtr clCreateCommandQueue(IntPtr ctx, IntPtr dev, ulong props, out int err);
        [DllImport(DLL)] public static extern IntPtr clCreateProgramWithSource(IntPtr ctx, uint count, string[] src, IntPtr[] lens, out int err);
        [DllImport(DLL)] public static extern int clBuildProgram(IntPtr prog, uint n, IntPtr[] devs, string opts, IntPtr notify, IntPtr user);
        [DllImport(DLL)] public static extern int clGetProgramBuildInfo(IntPtr prog, IntPtr dev, uint param, IntPtr size, StringBuilder log, out IntPtr ret);
        [DllImport(DLL)] public static extern IntPtr clCreateKernel(IntPtr prog, string name, out int err);
        [DllImport(DLL)] public static extern IntPtr clCreateBuffer(IntPtr ctx, ulong flags, IntPtr size, IntPtr host, out int err);
        [DllImport(DLL, EntryPoint = "clSetKernelArg")] public static extern int ArgMem(IntPtr k, uint i, IntPtr size, ref IntPtr v);
        [DllImport(DLL, EntryPoint = "clSetKernelArg")] public static extern int ArgInt(IntPtr k, uint i, IntPtr size, ref int v);
        [DllImport(DLL)] public static extern int clEnqueueNDRangeKernel(IntPtr q, IntPtr k, uint dim, IntPtr[] off, IntPtr[] gsz, IntPtr[] lsz, uint nev, IntPtr evs, IntPtr ev);
        [DllImport(DLL)] public static extern int clFinish(IntPtr q);
        [DllImport(DLL)] public static extern int clReleaseMemObject(IntPtr m);
        [DllImport(DLL)] public static extern int clReleaseKernel(IntPtr k);
        [DllImport(DLL)] public static extern int clReleaseProgram(IntPtr p);
        [DllImport(DLL)] public static extern int clReleaseCommandQueue(IntPtr q);
        [DllImport(DLL)] public static extern int clReleaseContext(IntPtr c);
    }

    // ------------------------------------------------------------------
    //  Janela de render do teste de estresse (visual como FurMark / Cinebench)
    // ------------------------------------------------------------------
    class BenchCanvas : Control
    {
        public bool HudOnly; public int Kind; public double Rate, Temp = double.NaN, Elapsed, Total; public string Unit = "", Extra = ""; public long Errors;
        static readonly string[] Names = { "GPU - render 3D de pelos (OpenGL)", "CPU - render 3D por blocos (ray tracing)", "FPU - malha de ondas em ponto flutuante", "RAM - mapa de memória", "SSD - mapa de blocos do disco" };
        Timer tm = new Timer();
        DateTime t0 = DateTime.UtcNow, fpsT = DateTime.UtcNow; int frames; double fps;
        // pelos
        const int HN = 1400;
        double[] hu = new double[HN], hv = new double[HN], hs = new double[HN];
        Pen[] furPens = new Pen[8];
        // render por blocos (ray tracing)
        const int W = 1024, H = 1024, B = 32, NBX = W / B, NBY = H / B, NB = NBX * NBY;
        int[] pix = new int[W * H]; int[] bstate = new int[NB];
        double lastPass, bestPass; DateTime passStart = DateTime.UtcNow;
        int nextB, doneB, pass; DateTime finishedAt = DateTime.MinValue; volatile bool quit; object lk = new object();
        Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppRgb);

        public BenchCanvas(int kind, int threads)
        {
            Kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
            Random r = new Random(11);
            for (int i = 0; i < HN; i++) { hu[i] = r.NextDouble() * Math.PI * 2; hv[i] = r.NextDouble() * Math.PI * 2; hs[i] = r.NextDouble(); }
            for (int i = 0; i < 8; i++) furPens[i] = new Pen(Color.FromArgb(255, 90 + i * 22, 10 + i * 10, 0), 1f);
            for (int i = 0; i < pix.Length; i++) pix[i] = unchecked((int)0xFF303030);
            tm.Interval = 50; tm.Tick += delegate { Invalidate(); }; tm.Start();
            if (kind == 2)
            {
                fpuN = Math.Max(1, threads);
                for (int i = 0; i < fpuN; i++)
                {
                    Th.Thread ft = new Th.Thread(FpuLoop); ft.IsBackground = true; ft.Start(i);
                }
            }
            if (kind == 1)
                for (int i = 0; i < Math.Max(1, threads); i++)
                {
                    Th.Thread t = new Th.Thread(CpuLoop); t.IsBackground = true; t.Priority = Th.ThreadPriority.Normal; t.Start();
                }
        }

        public void SavePng()
        {
            using (SaveFileDialog sd = new SaveFileDialog())
            {
                sd.Filter = "PNG|*.png"; sd.FileName = "render_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".png";
                if (sd.ShowDialog() != DialogResult.OK) return;
                try
                {
                    using (Bitmap c = new Bitmap(W, H, PixelFormat.Format32bppRgb))
                    {
                        BitmapData d = c.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                        Marshal.Copy(pix, 0, d.Scan0, pix.Length); c.UnlockBits(d); c.Save(sd.FileName, ImageFormat.Png);
                    }
                }
                catch (Exception) { }
            }
        }

        protected override void Dispose(bool d)
        {
            quit = true; tm.Stop();
            if (d) { bmp.Dispose(); foreach (Pen p in furPens) p.Dispose(); }
            base.Dispose(d);
        }

        // ---------- FPU: campo de ondas calculado em ponto flutuante pelas threads; a malha desenhada vem deste campo ----------
        const int FN = 320; float[] field = new float[FN * FN]; volatile int fpuN = 1; double ph;
        void FpuLoop(object o)
        {
            int w = (int)o;
            while (!quit && !Bench.Stop)
            {
                int n = fpuN; double p = ph;
                for (int i = w; i < FN; i += n)
                {
                    double x = (i - FN / 2.0) / (FN / 2.0);
                    for (int j = 0; j < FN; j++)
                    {
                        double z = (j - FN / 2.0) / (FN / 2.0), r = Math.Sqrt(x * x + z * z);
                        double h = 0.35 * Math.Sin(6 * r - p * 2) * Math.Exp(-r * 1.2) + 0.2 * Math.Sin(3 * x + p) * Math.Cos(3 * z + p * 0.7);
                        for (int k = 1; k < 8; k++) h += 0.01 * Math.Sin(h * k + x) * Math.Cos(z * k + p);
                        field[i * FN + j] = (float)h;
                    }
                }
                Th.Interlocked.Add(ref Bench.Counter, (long)(FN / n + 1) * FN * 200);
                if (w == 0) ph += 0.08;
            }
        }

        // ---------- CPU: ray tracing 3D por blocos (como o Cinebench / Blender), todas as threads renderizando ----------
        // Cena: esfera de ouro espelhada sobre grama, floresta ao fundo (tudo procedural; os reflexos sao calculados por ray tracing na CPU)
        double camE0, camE1, camE2, camF0, camF1, camF2, camR0, camR2, camU0, camU1, camU2; int[] order; const int SPP = 256;
        const double LX = -0.6061, LY = 0.3435, LZ = 0.7274;   // direcao do sol (normalizada)

        void SetupPass()
        {
            camE0 = 0; camE1 = 1.3; camE2 = -5.2;
            double tx = 0, ty = 0.25, tz = 0;
            double fx = tx - camE0, fy = ty - camE1, fz = tz - camE2, fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            camF0 = fx / fl; camF1 = fy / fl; camF2 = fz / fl;
            double rl = Math.Sqrt(camF0 * camF0 + camF2 * camF2);
            camR0 = -camF2 / rl; camR2 = camF0 / rl;
            camU0 = -camR2 * camF1; camU1 = camR2 * camF0 - camR0 * camF2; camU2 = camR0 * camF1;
        }

        static double H2(int x, int y)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263;
                n = (n ^ (n >> 13)) * 1274126177; n ^= n >> 16;
                return (n & 0xFFFFFF) / 16777216.0;
            }
        }
        static double Noise(double x, double y)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y); int xi = (int)fx, yi = (int)fy;
            double u = x - fx, v = y - fy; u = u * u * (3 - 2 * u); v = v * v * (3 - 2 * v);
            double a = H2(xi, yi), b = H2(xi + 1, yi), c = H2(xi, yi + 1), d = H2(xi + 1, yi + 1);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }
        static double Fbm(double x, double y, int oct)
        {
            double sum = 0, amp = 0.5, tot = 0;
            for (int i = 0; i < oct; i++) { sum += amp * Noise(x, y); tot += amp; x = x * 2.03 + 17.1; y = y * 2.03 + 5.3; amp *= 0.5; }
            return sum / tot;
        }
        static double Sm(double a, double b, double x) { double t = (x - a) / (b - a); if (t < 0) t = 0; else if (t > 1) t = 1; return t * t * (3 - 2 * t); }

        // ambiente: ceu quente + floresta densa (copas, duas camadas de troncos) ao redor, com o sol baixo
        static void Env(double dx, double dy, double dz, out double R, out double G, out double Bl)
        {
            double e = Math.Abs(dy);
            double sd = dx * LX + dy * LY + dz * LZ; if (sd < 0) sd = 0;
            double k = Sm(0, 0.9, e);
            double sr = 0.95 - 0.38 * k, sg = 0.9 - 0.2 * k, sb = 0.78 + 0.17 * k;
            double glow = Math.Pow(sd, 10) * 0.9; sr += glow; sg += glow * 0.75; sb += glow * 0.4;
            if (sd > 0.9992) { sr += 9; sg += 7.5; sb += 5; }
            double phi = Math.Atan2(dz, dx);
            double dens = Fbm(phi * 11, e * 9, 5) + (0.5 - e) * 0.9;
            double thr = 0.45 + e * 0.55;
            double fol = Sm(thr - 0.04, thr + 0.04, dens);
            double n1 = Fbm(phi * 26 + 3, e * 24, 4), n2 = Fbm(phi * 7 + 9, e * 6, 3);
            double lit = Sm(0.56, 0.92, n1 * 0.75 + 0.3 * n2 + 0.45 * sd * sd + 0.05 + e * 0.3);
            double dk = 0.45 + 0.55 * Sm(0.02, 0.3, e);
            double fr = (0.012 + 0.25 * lit) * dk, fg = (0.035 + 0.34 * lit) * dk, fb = (0.008 + 0.05 * lit) * dk;
            double tr = 0, tg = 0, tb = 0, tw = 0;
            for (int ly = 0; ly < 2; ly++)
            {
                double cell = ly == 0 ? 0.3 : 0.1, kk = phi / cell, ci = Math.Floor(kk), hh = H2((int)ci, 7 + ly * 31);
                if (hh > (ly == 0 ? 0.5 : 0.45) && e < 0.5)
                {
                    double ctr = ci + 0.2 + 0.6 * H2((int)ci, 9 + ly * 31);
                    double wd = (ly == 0 ? 0.06 + 0.14 * H2((int)ci, 11) : 0.08 + 0.1 * H2((int)ci, 11)) - e * (ly == 0 ? 0.06 : 0.05);
                    double dd = Math.Abs(kk - ctr);
                    if (dd < wd)
                    {
                        double w = (1 - Sm(wd * 0.85, wd, dd)) * (1 - Sm(0.38, 0.5, e));
                        double bark = Fbm(phi * 150 + ly * 7, e * 7, 3), rel = (kk - ctr) / wd;
                        double side = Sm(-1, 1, -rel * 0.9 + 0.5 * sd + 0.2), dark = ly == 0 ? 1.0 : 0.5;
                        double lr = (0.06 + 0.34 * side) * (0.6 + 0.7 * bark) * dark, lg = (0.045 + 0.22 * side) * (0.6 + 0.7 * bark) * dark, lb = (0.03 + 0.12 * side) * (0.6 + 0.7 * bark) * dark;
                        if (ly == 0 || tw < 0.5) { tr = lr; tg = lg; tb = lb; tw = w; if (ly == 0 && w > 0.5) ly = 2; }
                    }
                }
            }
            double r = sr * (1 - fol) + fr * fol, g = sg * (1 - fol) + fg * fol, b = sb * (1 - fol) + fb * fol;
            if (tw > 0) { r = r * (1 - tw) + tr * tw; g = g * (1 - tw) + tg * tw; b = b * (1 - tw) + tb * tw; }
            R = r; G = g; Bl = b;
        }

        static bool HitSphere(double ox, double oy, double oz, double dx, double dy, double dz, out double t)
        {
            double lx = ox, ly = oy - 1.0, lz = oz;
            double b = lx * dx + ly * dy + lz * dz, c = lx * lx + ly * ly + lz * lz - 1.0, disc = b * b - c;
            t = 0;
            if (disc <= 0) return false;
            t = -b - Math.Sqrt(disc);
            return t > 1e-4;
        }

        static void Trace(double ox, double oy, double oz, double dx, double dy, double dz, Random rnd, ref long rays, out double R, out double G, out double Bl)
        {
            double ar = 0, ag = 0, ab = 0, tr = 1, tg = 1, tb = 1;
            for (int depth = 0; depth < 4; depth++)
            {
                rays++;
                double ts, tgd = 1e30; bool hs = HitSphere(ox, oy, oz, dx, dy, dz, out ts);
                if (dy < -1e-6) tgd = -oy / dy;
                if (!hs) ts = 1e30;
                if (ts >= 1e29 && (tgd > 70 || tgd < 1e-4))
                {
                    double er, eg, eb; Env(dx, dy, dz, out er, out eg, out eb);
                    ar += tr * er; ag += tg * eg; ab += tb * eb; break;
                }
                if (hs && ts < tgd)
                {
                    double px = ox + dx * ts, py = oy + dy * ts, pz = oz + dz * ts, nx = px, ny = py - 1.0, nz = pz;
                    double dn = dx * nx + dy * ny + dz * nz;
                    double cs = -dn; if (cs < 0) cs = 0; double fw = Math.Pow(1 - cs, 5) * 0.5;
                    tr *= (1.0 + (1 - 1.0) * fw) * 0.97; tg *= (0.78 + (1 - 0.78) * fw) * 0.97; tb *= (0.34 + (1 - 0.34) * fw) * 0.97;
                    double rx = dx - 2 * dn * nx + (rnd.NextDouble() - 0.5) * 0.006, ry = dy - 2 * dn * ny + (rnd.NextDouble() - 0.5) * 0.006, rz = dz - 2 * dn * nz + (rnd.NextDouble() - 0.5) * 0.006;
                    double rl = Math.Sqrt(rx * rx + ry * ry + rz * rz);
                    ox = px + nx * 1e-3; oy = py + ny * 1e-3; oz = pz + nz * 1e-3; dx = rx / rl; dy = ry / rl; dz = rz / rl;
                    continue;
                }
                {
                    double px = ox + dx * tgd, pz = oz + dz * tgd;
                    // grama procedural (varias oitavas de ruido = lamina fina + manchas grandes)
                    double big = Fbm(px * 1.1, pz * 1.1, 3), bl = Fbm(px * 60, pz * 40, 4), bl2 = Fbm(px * 140 + 3, pz * 90, 2);
                    double gl = 0.3 + 0.4 * bl + 0.3 * big + 0.12 * bl2; if (gl > 1) gl = 1;
                    double cr = 0.06 + 0.34 * gl, cg = 0.2 + 0.42 * gl, cb = 0.03 + 0.09 * gl;
                    double ex = Fbm(px * 60 + 0.7, pz * 40, 3) - bl, ez = Fbm(px * 60, pz * 40 + 0.7, 3) - bl;
                    double nx = -ex * 4, nz = -ez * 4, ny = 1, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz); nx /= nl; ny /= nl; nz /= nl;
                    double diff = nx * LX + ny * LY + nz * LZ; if (diff < 0) diff = 0;
                    // sombra da esfera (sol com tamanho: raio de sombra sorteado) e sombra das arvores (faixas alongadas)
                    double vis = 1, st;
                    rays++;
                    double jx = LX + (rnd.NextDouble() - 0.5) * 0.07, jy = LY + (rnd.NextDouble() - 0.5) * 0.07, jz = LZ + (rnd.NextDouble() - 0.5) * 0.07, jl = Math.Sqrt(jx * jx + jy * jy + jz * jz);
                    if (HitSphere(px, 1e-3, pz, jx / jl, jy / jl, jz / jl, out st)) vis = 0.05;
                    double aa = px * 0.7673 + pz * 0.6413, bb = -px * 0.6413 + pz * 0.7673;
                    double mask = 0.2 + 0.8 * Sm(0.38, 0.55, Fbm(aa * 0.55 + 4, bb * 0.14, 3)); mask *= 1 - 0.7 * Sm(7, 11, pz);
                    // oclusao ambiente por 3 raios + luz dourada refletida pela esfera
                    double occ = 0, gold = 0;
                    for (int q = 0; q < 3; q++)
                    {
                        rays++;
                        double u1 = rnd.NextDouble(), u2 = rnd.NextDouble(), rr = Math.Sqrt(u1), ang = 6.2831853 * u2;
                        double hx = rr * Math.Cos(ang), hz = rr * Math.Sin(ang), hy = Math.Sqrt(1 - u1);
                        double qt;
                        if (HitSphere(px, 1e-3, pz, hx, hy, hz, out qt)) { occ += 1; gold += 1; }
                    }
                    double ao = 1 - 0.55 * occ / 3.0, gb = gold / 3.0;
                    double wr = diff * vis * mask * 1.55 + ao * 0.34 + gb * 0.55, wg = diff * vis * mask * 1.3 + ao * 0.4 + gb * 0.4, wb = diff * vis * mask * 0.85 + ao * 0.46 + gb * 0.12;
                    ar += tr * cr * wr; ag += tg * cg * wg; ab += tb * cb * wb;
                    break;
                }
            }
            R = ar; G = ag; Bl = ab;
        }

        int Take()
        {
            lock (lk)
            {
                if (order == null)
                {
                    // espiral no sentido horario, do centro para fora
                    List<int> l = new List<int>();
                    int[] sdx = { 1, 0, -1, 0 }, sdy = { 0, 1, 0, -1 };
                    int sx = NBX / 2, sy = NBY / 2, sdir = 0, slen = 1, guard = 0;
                    l.Add(sy * NBX + sx);
                    while (l.Count < NB && guard++ < 10000)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            for (int st = 0; st < slen; st++)
                            {
                                sx += sdx[sdir]; sy += sdy[sdir];
                                if (sx >= 0 && sx < NBX && sy >= 0 && sy < NBY) l.Add(sy * NBX + sx);
                            }
                            sdir = (sdir + 1) & 3;
                        }
                        slen++;
                    }
                    order = l.ToArray(); SetupPass();
                }
                if (nextB < NB) { int b = order[nextB++]; bstate[b] = 1; return b; }
                if (doneB >= NB && (DateTime.UtcNow - finishedAt).TotalSeconds > 0.8)
                {
                    pass++; SetupPass();
                    nextB = 0; doneB = 0; passStart = DateTime.UtcNow;
                    for (int i = 0; i < NB; i++) bstate[i] = 0;
                    for (int i = 0; i < pix.Length; i++) pix[i] = unchecked((int)0xFF202020);
                }
                return -1;
            }
        }
        void CpuLoop()
        {
            Random rnd = new Random(Th.Thread.CurrentThread.ManagedThreadId * 7919 + Environment.TickCount);
            while (!quit && !Bench.Stop)
            {
                int b = Take();
                if (b < 0) { Th.Thread.Sleep(30); continue; }
                int x0 = (b % NBX) * B, y0 = (b / NBX) * B; long rays = 0;
                double ex = camE0, ey = camE1, ez = camE2, fx = camF0, fy = camF1, fz = camF2, rx = camR0, rz = camR2, ux = camU0, uy = camU1, uz = camU2;
                double th = 0.56, asp = W / (double)H;
                for (int y = y0; y < y0 + B && !quit && !Bench.Stop; y++)
                    for (int x = x0; x < x0 + B; x++)
                    {
                        double sr = 0, sg = 0, sb = 0;
                        for (int s = 0; s < SPP; s++)
                        {
                            double u = ((x + rnd.NextDouble()) / W * 2 - 1) * asp * th, v = (1 - (y + rnd.NextDouble()) / H * 2) * th;
                            double dx = fx + rx * u + ux * v, dy = fy + uy * v, dz = fz + rz * u + uz * v, dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            double cr, cg, cb;
                            Trace(ex, ey, ez, dx / dl, dy / dl, dz / dl, rnd, ref rays, out cr, out cg, out cb);
                            sr += cr; sg += cg; sb += cb;
                        }
                        int ir = (int)(255 * Math.Sqrt(Math.Min(1, 0.85 * sr / SPP))), ig = (int)(255 * Math.Sqrt(Math.Min(1, 0.85 * sg / SPP))), ib = (int)(255 * Math.Sqrt(Math.Min(1, 0.85 * sb / SPP)));
                        pix[y * W + x] = unchecked((int)0xFF000000) | (ir << 16) | (ig << 8) | ib;
                    }
                Th.Interlocked.Add(ref Bench.Counter, rays);
                lock (lk) { bstate[b] = 2; doneB++; if (doneB >= NB) { finishedAt = DateTime.UtcNow; lastPass = (finishedAt - passStart).TotalSeconds; if (bestPass <= 0 || lastPass < bestPass) bestPass = lastPass; } }
            }
        }

        // ---------- desenho ----------
        static PointF Proj(double x, double y, double z, double ox, double oy, double sc, out double depth)
        {
            depth = z;
            double k = sc / (z + 4.2);
            return new PointF((float)(ox + x * k), (float)(oy - y * k));
        }

        void DrawFur(Graphics g, Rectangle r, double t)
        {
            double ay = t * 0.9, ax = 0.55 + 0.3 * Math.Sin(t * 0.5);
            double cay = Math.Cos(ay), say = Math.Sin(ay), cax = Math.Cos(ax), sax = Math.Sin(ax);
            double ox = r.X + r.Width / 2.0, oy = r.Y + r.Height / 2.0, sc = Math.Min(r.Width, r.Height) * 0.62;
            using (GraphicsPath gl = new GraphicsPath())
            {
                gl.AddEllipse((float)(ox - sc * 0.55), (float)(oy - sc * 0.55), (float)(sc * 1.1), (float)(sc * 1.1));
                using (PathGradientBrush pb = new PathGradientBrush(gl))
                {
                    pb.CenterColor = Color.FromArgb(70, 120, 30, 0); pb.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                    g.FillPath(pb, gl);
                }
            }
            for (int i = 0; i < HN; i++)
            {
                double u = hu[i], v = hv[i];
                double R = 1.0, rr = 0.42;
                double nx = Math.Cos(v) * Math.Cos(u), ny = Math.Sin(v), nz = Math.Cos(v) * Math.Sin(u);
                double px = (R + rr * Math.Cos(v)) * Math.Cos(u), py = rr * Math.Sin(v), pz = (R + rr * Math.Cos(v)) * Math.Sin(u);
                double len = 0.24 + 0.08 * Math.Sin(t * 4 + hs[i] * 6.28);
                double ex = px + nx * len + 0.03 * Math.Sin(t * 3 + i), ey = py + ny * len, ez = pz + nz * len;
                // gira em Y e depois em X
                double x1 = px * cay + pz * say, z1 = -px * say + pz * cay, y1 = py * cax - z1 * sax, z2 = py * sax + z1 * cax;
                double x2 = ex * cay + ez * say, z3 = -ex * say + ez * cay, y2 = ey * cax - z3 * sax, z4 = ey * sax + z3 * cax;
                double d1, d2;
                PointF a = Proj(x1, y1, z2, ox, oy, sc, out d1), b = Proj(x2, y2, z4, ox, oy, sc, out d2);
                int idx = (int)((1 - (d2 + 1.6) / 3.2) * 7); if (idx < 0) idx = 0; if (idx > 7) idx = 7;
                g.DrawLine(furPens[idx], a, b);
            }
        }

        void DrawWave(Graphics g, Rectangle r, double t)
        {
            int n = 30;
            double ay = t * 0.5, ax = 0.9;
            double cay = Math.Cos(ay), say = Math.Sin(ay), cax = Math.Cos(ax), sax = Math.Sin(ax);
            double ox = r.X + r.Width / 2.0, oy = r.Y + r.Height / 2.0 + 20, sc = Math.Min(r.Width, r.Height) * 0.75;
            PointF[,] pts = new PointF[n, n]; double[,] hh = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double x = (i - n / 2.0) / (n / 2.0) * 1.3, z = (j - n / 2.0) / (n / 2.0) * 1.3;
                    double h = field[(int)(i * (FN - 1) / (n - 1.0)) * FN + (int)(j * (FN - 1) / (n - 1.0))] * 1.4;
                    double x1 = x * cay + z * say, z1 = -x * say + z * cay, y1 = h * cax - z1 * sax, z2 = h * sax + z1 * cax, d;
                    pts[i, j] = Proj(x1, y1, z2, ox, oy, sc, out d); hh[i, j] = h;
                }
            using (Pen p1 = new Pen(Color.FromArgb(60, 200, 90)), p2 = new Pen(Color.FromArgb(120, 255, 140)), p3 = new Pen(Color.FromArgb(220, 255, 200)))
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        Pen p = hh[i, j] > 0.2 ? p3 : (hh[i, j] > 0 ? p2 : p1);
                        if (i + 1 < n) g.DrawLine(p, pts[i, j], pts[i + 1, j]);
                        if (j + 1 < n) g.DrawLine(p, pts[i, j], pts[i, j + 1]);
                    }
        }

        void DrawCpu(Graphics g, Rectangle r)
        {
            System.Drawing.Imaging.BitmapData d = bmp.LockBits(new Rectangle(0, 0, W, H), System.Drawing.Imaging.ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            Marshal.Copy(pix, 0, d.Scan0, pix.Length);
            bmp.UnlockBits(d);
            float s = Math.Min(r.Width / (float)W, r.Height / (float)H);
            float w = W * s, h = H * s, x0 = r.X + (r.Width - w) / 2, y0 = r.Y + (r.Height - h) / 2;
            g.InterpolationMode = s < 1f ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
            g.DrawImage(bmp, x0, y0, w, h);
            using (Pen p = new Pen(Color.White, 2f))
                for (int b = 0; b < NB; b++)
                    if (bstate[b] == 1) g.DrawRectangle(p, x0 + (b % NBX) * B * s, y0 + (b / NBX) * B * s, B * s, B * s);
            using (Pen fr = new Pen(Color.FromArgb(120, 120, 120))) g.DrawRectangle(fr, x0, y0, w, h);
            string st = "Render #" + (pass + 1) + "   " + SPP + " amostras/pixel   blocos: " + doneB + "/" + NB + "   tempo: " + (doneB >= NB ? lastPass : (DateTime.UtcNow - passStart).TotalSeconds).ToString("0.0") + " s";
            if (lastPass > 0) st += "   último: " + lastPass.ToString("0.0") + " s   melhor: " + bestPass.ToString("0.0") + " s";
            TextRenderer.DrawText(g, Lang.T(st), SystemFonts.DefaultFont, new Point((int)x0 + 4, (int)y0 + 4), Color.White, Color.FromArgb(150, 0, 0, 0));
        }

        static Color Mix(Color a, Color b, double t) { t = Math.Max(0, Math.Min(1, t)); return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }

        // RAM: mapa REAL dos blocos de 64 MB que o teste esta usando (cada quadrado = um bloco) + palavras reais lidas da memoria
        void DrawRam(Graphics g, Rectangle r, double t)
        {
            g.SmoothingMode = SmoothingMode.None;
            byte[] st = Bench.RamState; int[] pr = Bench.RamProg; int nb = st.Length;
            Color dark = Color.FromArgb(30, 30, 34), cWrite = Color.FromArgb(50, 140, 255), cVer = Color.FromArgb(240, 190, 40), cOk = Color.FromArgb(50, 190, 90), cBad = Color.FromArgb(235, 50, 50);
            Rectangle map = new Rectangle(r.X + 10, r.Y + 6, r.Width - 20, Math.Max(60, r.Height * 11 / 20));
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(16, 16, 20))) g.FillRectangle(bg, map);
            if (nb == 0)
            {
                TextRenderer.DrawText(g, Lang.T("Reservando a memória do teste..."), SystemFonts.DefaultFont, new Point(map.X + 8, map.Y + 8), Color.Gray);
            }
            else
            {
                int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(nb * map.Width / (double)map.Height)));
                int rows = (int)Math.Ceiling(nb / (double)cols);
                float cw = map.Width / (float)cols, ch = map.Height / (float)rows;
                using (SolidBrush br = new SolidBrush(dark))
                    for (int i = 0; i < nb; i++)
                    {
                        float x = map.X + (i % cols) * cw, y = map.Y + (i / cols) * ch;
                        byte s0 = st[i]; int pg = i < pr.Length ? pr[i] : 0;
                        br.Color = s0 == 3 ? cOk : s0 == 4 ? cBad : dark; g.FillRectangle(br, x + 1, y + 1, Math.Max(1, cw - 2), Math.Max(1, ch - 2));
                        if (s0 == 1 || s0 == 2)
                        {
                            br.Color = s0 == 1 ? cWrite : cVer;
                            float hh = Math.Max(1, (ch - 2) * pg / 1000f);
                            g.FillRectangle(br, x + 1, y + 1 + (ch - 2) - hh, Math.Max(1, cw - 2), hh);
                        }
                    }
            }
            using (Pen fp = new Pen(Color.FromArgb(90, 90, 100))) g.DrawRectangle(fp, map);
            // legenda
            int lx = map.X, ly = map.Bottom + 6;
            string[] lt = { "gravando", "conferindo", "conferido sem erro", "erro" }; Color[] lc = { cWrite, cVer, cOk, cBad };
            for (int i = 0; i < 4; i++)
            {
                using (SolidBrush lb = new SolidBrush(lc[i])) g.FillRectangle(lb, lx, ly + 3, 10, 10);
                string tx = Lang.T(lt[i]); TextRenderer.DrawText(g, tx, SystemFonts.DefaultFont, new Point(lx + 14, ly), Color.Silver);
                lx += 14 + TextRenderer.MeasureText(tx, SystemFonts.DefaultFont).Width + 14;
            }
            string info = Lang.T("Padrão") + ": 0x" + Bench.RamPattern.ToString("X16") + "     " + Lang.T("Passagem") + " " + Bench.RamRound + "     " + Lang.T("Blocos") + ": " + nb + " x 64 MB";
            TextRenderer.DrawText(g, info, new Font("Consolas", 9f, FontStyle.Bold), new Point(map.X, ly + 20), Color.FromArgb(255, 200, 60));
            // palavras reais da memoria que esta sendo gravada/conferida agora
            long[] view = Bench.RamView; int vp = Bench.RamViewPos;
            if (view != null)
                using (Font f = new Font("Consolas", 9f))
                {
                    int top = ly + 40, rowsHex = Math.Max(0, (r.Bottom - top) / 15);
                    for (int row = 0; row < rowsHex; row++)
                    {
                        int idx = vp + row * 2;
                        if (idx + 1 >= view.Length) break;
                        string ln = "+0x" + ((long)idx * 8).ToString("X8") + "   0x" + view[idx].ToString("X16") + "   0x" + view[idx + 1].ToString("X16");
                        TextRenderer.DrawText(g, ln, f, new Point(map.X, top + row * 15), Color.FromArgb(Math.Max(70, 230 - row * 25), 120, 220, 140));
                    }
                }
        }

        List<double> ssdHist = new List<double>(); DateTime ssdHistT = DateTime.MinValue;
        // SSD: mapa REAL do arquivo de teste (gravado / lido e conferido / erro), cabecote na posicao real, regioes 4K acessadas e velocidade ao vivo
        void DrawSsd(Graphics g, Rectangle r, double t)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if ((DateTime.UtcNow - ssdHistT).TotalMilliseconds >= 150) { ssdHistT = DateTime.UtcNow; ssdHist.Add(Rate); if (ssdHist.Count > 160) ssdHist.RemoveAt(0); }
            int ph = Bench.SsdPhase, smb = Math.Max(1, Bench.SsdMb); byte[] map = Bench.SsdMap; int[] hits = Bench.SsdHit;
            Color cW = Color.FromArgb(70, 150, 255), cR = Color.FromArgb(60, 200, 110), cK = Color.FromArgb(240, 200, 40), cE = Color.FromArgb(235, 50, 50);
            Color acc = ph == 2 ? cK : (ph == 1 ? cR : cW);
            string[] pn = { "Escrita sequencial (1 MB)", "Leitura sequencial + conferência", "Leitura aleatória 4K" };
            TextRenderer.DrawText(g, Lang.T(pn[Math.Max(0, Math.Min(2, ph))]), SystemFonts.DefaultFont, new Point(r.X + 12, r.Y + 2), acc);
            // 1) faixa do arquivo: cada segmento e uma fatia real do arquivo de teste
            Rectangle lane = new Rectangle(r.X + 10, r.Y + 22, r.Width - 20, 30);
            using (SolidBrush lb = new SolidBrush(Color.FromArgb(24, 24, 28))) g.FillRectangle(lb, lane);
            int segs = 128; float sw = lane.Width / (float)segs;
            for (int i = 0; i < segs; i++)
            {
                byte v = i < map.Length ? map[i] : (byte)0;
                if (v == 0) continue;
                using (SolidBrush sb = new SolidBrush(v == 3 ? cE : v == 2 ? cR : cW)) g.FillRectangle(sb, lane.X + i * sw, lane.Y + 2, Math.Max(1, sw - 0.5f), lane.Height - 4);
            }
            if (ph < 2)
                using (Pen hp = new Pen(Color.White, 2f)) { float hx = lane.X + Math.Min(Bench.SsdPos + 1, smb) * lane.Width / (float)smb; g.DrawLine(hp, hx, lane.Y - 2, hx, lane.Bottom + 2); }
            g.DrawRectangle(Pens.DimGray, lane);
            // 2) grafico de velocidade
            Rectangle gr = new Rectangle(r.X + 10, lane.Bottom + 12, (r.Width - 30) * 3 / 5, r.Bottom - lane.Bottom - 20);
            using (SolidBrush gb = new SolidBrush(Color.FromArgb(18, 18, 22))) g.FillRectangle(gb, gr);
            using (Pen gp = new Pen(Color.FromArgb(50, 50, 58))) for (int i = 1; i < 4; i++) g.DrawLine(gp, gr.X, gr.Y + gr.Height * i / 4, gr.Right, gr.Y + gr.Height * i / 4);
            if (ssdHist.Count > 2)
            {
                double mx = 1; foreach (double v in ssdHist) if (v > mx) mx = v;
                PointF[] pts = new PointF[ssdHist.Count + 2];
                for (int i = 0; i < ssdHist.Count; i++) pts[i + 1] = new PointF(gr.X + i * gr.Width / 159f, gr.Bottom - (float)(ssdHist[i] / mx) * (gr.Height - 6) - 2);
                pts[0] = new PointF(pts[1].X, gr.Bottom); pts[pts.Length - 1] = new PointF(pts[pts.Length - 2].X, gr.Bottom);
                using (SolidBrush fb = new SolidBrush(Color.FromArgb(70, acc))) g.FillPolygon(fb, pts);
                using (Pen lp = new Pen(acc, 2f)) { PointF[] ln = new PointF[ssdHist.Count]; Array.Copy(pts, 1, ln, 0, ln.Length); g.DrawLines(lp, ln); }
            }
            g.DrawRectangle(Pens.DimGray, gr);
            // 3) mapa das regioes: nas fases sequenciais mostra o progresso real; na fase 4K acende as regioes realmente lidas
            Rectangle mp = new Rectangle(gr.Right + 10, gr.Y, r.Right - gr.Right - 20, gr.Height);
            int cols = 24, rows = Math.Max(4, mp.Height / 12); float cw = mp.Width / (float)cols, ch = mp.Height / (float)rows;
            int now = Environment.TickCount;
            using (SolidBrush mb = new SolidBrush(Color.Black))
                for (int i = 0; i < cols * rows; i++)
                {
                    Color c = Color.FromArgb(30, 30, 36);
                    if (ph == 2)
                    {
                        int hh = hits[(int)((long)i * 512 / (cols * rows))];
                        if (hh != 0) { int age = unchecked(now - hh); if (age >= 0 && age < 1500) c = Mix(cK, Color.FromArgb(30, 30, 36), age / 1500.0); }
                    }
                    else
                    {
                        byte v = map[Math.Min(map.Length - 1, (int)((long)i * 128 / (cols * rows)))];
                        c = v == 3 ? cE : v == 2 ? cR : v == 1 ? cW : c;
                    }
                    mb.Color = c;
                    g.FillRectangle(mb, mp.X + (i % cols) * cw + 1, mp.Y + (i / cols) * ch + 1, cw - 2, ch - 2);
                }
        }

        void DrawGrid(Graphics g, Rectangle r, int cols, int rows, double t)
        {
            int cells = cols * rows;
            float cw = r.Width / (float)cols, ch = r.Height / (float)rows;
            Color[] pal = { Color.FromArgb(40, 90, 200), Color.FromArgb(40, 170, 80), Color.FromArgb(200, 160, 30), Color.FromArgb(150, 60, 190), Color.FromArgb(30, 160, 170), Color.FromArgb(200, 90, 40) };
            int pos = (int)(t * (Kind == 3 ? 220 : 160));
            int cur = pos / cells, head = pos % cells;
            string ex = Extra ?? "";
            bool rnd = Kind == 4 && ex.IndexOf("4K") >= 0, read = Kind == 4 && ex.IndexOf("Leitura") >= 0 && !rnd;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(28, 28, 28)), br = new SolidBrush(Color.Black))
            {
                for (int i = 0; i < cells; i++)
                {
                    int c = i < head ? cur : cur - 1;
                    Color col = c < 0 ? Color.FromArgb(28, 28, 28) : pal[(Kind == 4 ? (read ? 1 : 0) : c) % pal.Length];
                    if (rnd) { int hsh = (int)(unchecked((uint)(i + (int)(t * 12)) * 2654435761u) >> 24); col = hsh < 90 ? Color.FromArgb(230, 200, 40) : Color.FromArgb(28, 28, 28); }
                    if (i == head) col = Color.White;
                    br.Color = col;
                    g.FillRectangle(br, r.X + (i % cols) * cw + 1, r.Y + (i / cols) * ch + 1, cw - 2, ch - 2);
                }
                if (Errors > 0)
                {
                    br.Color = Color.Red;
                    for (int k = 0; k < Math.Min(Errors, 40); k++)
                    {
                        int i = (int)((k * 977L + 13) % cells);
                        g.FillRectangle(br, r.X + (i % cols) * cw + 1, r.Y + (i / cols) * ch + 1, cw - 2, ch - 2);
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(10, 10, 12));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double t = (DateTime.UtcNow - t0).TotalSeconds;
            frames++;
            if ((DateTime.UtcNow - fpsT).TotalSeconds >= 1) { fps = frames / (DateTime.UtcNow - fpsT).TotalSeconds; frames = 0; fpsT = DateTime.UtcNow; }
            Rectangle area = new Rectangle(10, 34, Width - 20, Height - 34 - 74);
            if (!HudOnly) switch (Kind)
            {
                case 0: DrawFur(g, area, t); break;
                case 1: DrawCpu(g, area); break;
                case 2: DrawWave(g, area, t); break;
                case 3: DrawRam(g, area, t); break;
                default: DrawSsd(g, area, t); break;
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Font f = new Font("Tahoma", 10f, FontStyle.Bold), fb = new Font("Tahoma", 16f, FontStyle.Bold), fs = new Font("Tahoma", 8.25f))
            {
                TextRenderer.DrawText(g, Lang.T(Names[Kind < 5 ? Kind : 0]), f, new Point(12, 8), Color.FromArgb(255, 200, 60));
                string rt = Rate.ToString("0.0") + " " + Unit;
                TextRenderer.DrawText(g, rt, fb, new Point(12, Height - 70), Color.White);
                string tt = double.IsNaN(Temp) ? "-" : Temp.ToString("0") + " °C";
                int es = (int)Elapsed, tot = (int)Total;
                string tm2 = (es / 60).ToString("00") + ":" + (es % 60).ToString("00") + (tot > 0 ? " / " + (tot / 60).ToString("00") + ":" + (tot % 60).ToString("00") : "");
                string info = "Temperatura: " + tt + "     Tempo: " + tm2 + "     Erros: " + Errors + (Kind == 0 || Kind == 2 ? "     FPS do render: " + fps.ToString("0") : "") + (Kind == 1 ? "     Pontos: " + (Rate * 100).ToString("0") + " pts" : "");
                TextRenderer.DrawText(g, Lang.T(info), fs, new Point(12, Height - 36), Color.FromArgb(200, 200, 200));
                if (!string.IsNullOrEmpty(Extra)) TextRenderer.DrawText(g, Lang.T(Extra), fs, new Point(12, Height - 20), Color.FromArgb(150, 150, 150));
            }
        }
    }

    class RenderForm : XpForm
    {
        public BenchCanvas Cv; public GlCanvas Gl;
        public RenderForm(int kind, int threads)
        {
            Text = "BunoTunner - Render do teste (Esc fecha e para)";
            Size = kind == 1 ? new Size(720, 760) : new Size(700, 560);
            Cv = new BenchCanvas(kind, threads);
            if (kind == 0)
            {
                Cv.HudOnly = true; Cv.Dock = DockStyle.Bottom; Cv.Height = 10; Cv.Visible = false;
                Gl = new GlCanvas(); Gl.Dock = DockStyle.Fill; Gl.Hud = Cv;
                Controls.Add(Gl); Controls.Add(Cv);
            }
            else { Cv.Dock = DockStyle.Fill; Controls.Add(Cv); }
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            if (kind == 1)
            {
                ContextMenuStrip cm = new ContextMenuStrip();
                cm.Items.Add("Salvar imagem do render (PNG)", null, delegate { Cv.SavePng(); });
                Lang.Menu(cm); Cv.ContextMenuStrip = cm;
            }
        }
    }

    // ------------------------------------------------------------------
    //  PawnIO: driver assinado usado pelas versoes novas do LibreHardwareMonitor
    //  (temperatura da CPU, controle de ventoinhas da placa-mae, leitura SMN/timings).
    // ------------------------------------------------------------------
    static class PawnIo
    {
        public static bool LibUsesPawn()
        {
            try
            {
                foreach (Type t in typeof(LHM.Computer).Assembly.GetTypes())
                    if (t.Name.Equals("PawnIo", StringComparison.OrdinalIgnoreCase) || (t.Namespace != null && t.Namespace.EndsWith("PawnIo", StringComparison.OrdinalIgnoreCase))) return true;
            }
            catch (Exception) { }
            return false;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tpl);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        public static bool Installed()
        {
            string[] pf = { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetEnvironmentVariable("ProgramFiles(x86)") };
            foreach (string p in pf)
                if (!string.IsNullOrEmpty(p) && File.Exists(Path.Combine(Path.Combine(p, "PawnIO"), "PawnIOLib.dll"))) return true;
            try
            {
                IntPtr h = CreateFileW("\\\\.\\PawnIO", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h != IntPtr.Zero && h != (IntPtr)(-1)) { CloseHandle(h); return true; }
            }
            catch (Exception) { }
            return false;
        }

        // Baixa e executa o instalador oficial (pede permissao de administrador). Chamado pelo botao do menu Ventoinhas.
        public static void InstallNow(Control owner)
        {
            if (Installed()) { Dlg.Show(owner, "O PawnIO já está instalado.", "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (Dlg.Show(owner, "Baixar e instalar o PawnIO agora?\n\nO instalador oficial (github.com/namazso/PawnIO.Setup) será baixado e pedirá permissão de administrador.", "BunoTunner - PawnIO", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string exe = Path.Combine(Path.GetTempPath(), "PawnIO_setup.exe");
            owner.Enabled = false;
            Th.ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null; bool done = false;
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072 | (System.Net.SecurityProtocolType)768;
                    using (System.Net.WebClient wc = new System.Net.WebClient())
                        wc.DownloadFile("https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe", exe);
                    ProcessStartInfo psi = new ProcessStartInfo(exe); psi.UseShellExecute = true; psi.Verb = "runas";
                    using (Process p = Process.Start(psi)) { if (p != null) { p.WaitForExit(); done = true; } }
                }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    owner.BeginInvoke(new Action(delegate
                    {
                        owner.Enabled = true;
                        if (err != null)
                        {
                            if (Dlg.Show(owner, "Não foi possível instalar automaticamente (" + err + ").\nAbrir o download no navegador?", "BunoTunner", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                                try { Process.Start("https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe"); } catch (Exception) { }
                        }
                        else if (done) Dlg.Show(owner, Installed() ? "PawnIO instalado! Feche e abra o BunoTunner para os sensores, ventoinhas e timings passarem a usar o driver." : "O instalador foi fechado, mas o PawnIO não foi detectado. Tente novamente.", "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch (Exception) { }
            });
        }

        // Chamado em toda inicializacao: se o PawnIO nao estiver instalado, recomenda e leva ao download.
        public static void CheckAtStartup(IWin32Window owner)
        {
            if (Installed()) return;
            DialogResult r = Dlg.Show(owner,
                "Recomendamos a instalação do PawnIO.\n\n" +
                "Por que?\n" +
                "O PawnIO e um driver assinado e de código aberto que o LibreHardwareMonitor (usado pelo BunoTunner) precisa para acessar o hardware. " +
                "Sem ele ficam sem leitura: vários sensores de temperatura da CPU e da placa-mae, o controle das ventoinhas ligadas a placa-mae e os timings da memória RAM.\n\n" +
                "Deseja abrir o download oficial agora?\n(Sim = baixa o PawnIO_setup.exe; Não = continuar sem ele. Este aviso aparece a cada abertura enquanto ele não estiver instalado.)",
                "BunoTunner - PawnIO recomendado", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r != DialogResult.Yes) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception)
            {
                try { Process.Start("https://pawnio.eu"); } catch (Exception) { }
            }
            Dlg.Show(owner, "Depois do download, execute o PawnIO_setup.exe (como administrador), conclua a instalação e abra o BunoTunner novamente.", "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ------------------------------------------------------------------
    //  Fundos estaticos dos temas (Neon e GTA VI: pixel art; Steampunk: tubos de cobre, manometros e engrenagens)
    // ------------------------------------------------------------------
    static class Fx
    {
        // Fundos dos temas (estaticos, sem animacao): Neon e GTA VI em pixel art, Steampunk em arte realista
        static List<Control> targets = new List<Control>();
        static Bitmap sNeon, sVice, sSteam;
        static bool failed;
        public static bool On { get { return (Xp.Theme >= 3 && Xp.Theme <= 4) || Xp.Theme == 6; } }
        static Color A(int a, int r, int g, int b) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), r, g, b); }
        static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        static Color HexA(int a, int rgb) { return Color.FromArgb(a, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        static Color Lp(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        static Color Ramp(Color[] st, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            double f = t * (st.Length - 1);
            int i = (int)f;
            if (i >= st.Length - 1) return st[st.Length - 1];
            return Lp(st[i], st[i + 1], f - i);
        }

        public static void Hook(Control c)
        {
            if (targets.Contains(c)) return;
            targets.Add(c);
            c.Disposed += delegate { targets.Remove(c); };
            if (c.GetType() == typeof(Panel))
            {
                try { typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(c, true, null); } catch (Exception) { }
                c.Paint += delegate(object s, PaintEventArgs e) { Draw(c, e.Graphics); };
            }
        }

        static Bitmap Scene()
        {
            if (failed) return null;
            try
            {
                switch (Xp.Theme)
                {
                    case 3: if (sNeon == null) sNeon = MakeNeon(); return sNeon;
                    case 4: if (sSteam == null) sSteam = MakeSteam(); return sSteam;
                    case 6: if (sVice == null) sVice = MakeVice(); return sVice;
                }
            }
            catch (Exception) { failed = true; }
            return null;
        }

        public static void Draw(Control c, Graphics g)
        {
            if (!On) return;
            Form f = c.FindForm();
            if (f == null) return;
            Bitmap sc = Scene();
            if (sc == null) return;
            GraphicsState st = g.Save();
            try
            {
                Point p = f.PointToClient(c.PointToScreen(Point.Empty));
                g.TranslateTransform(-p.X, -p.Y);
                Rectangle R = f.ClientRectangle;
                g.InterpolationMode = Xp.Theme == 4 ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                // proporcao original preservada: a imagem cobre a janela sem esticar (corta o excesso, centralizado)
                double k = Math.Max(R.Width / (double)sc.Width, R.Height / (double)sc.Height);
                int sw = Math.Min(sc.Width, (int)Math.Ceiling(R.Width / k)), sh = Math.Min(sc.Height, (int)Math.Ceiling(R.Height / k));
                g.DrawImage(sc, R, (sc.Width - sw) / 2, (sc.Height - sh) / 2, sw, sh, GraphicsUnit.Pixel);
                int va = Xp.Theme == 3 ? 212 : Xp.Theme == 6 ? 218 : 165;
                using (SolidBrush veil = new SolidBrush(A(va, Xp.Face.R, Xp.Face.G, Xp.Face.B))) g.FillRectangle(veil, R);
            }
            catch (Exception) { }
            g.Restore(st);
        }

        // ================= pixel art =================
        static readonly int[] Bay = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        static void Sky(Bitmap b, int y0, int y1, Color[] st, int levels)
        {
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < b.Width; x++)
                {
                    double f = (y - y0) / (double)(y1 - y0) * levels;
                    int lo = (int)f;
                    if (f - lo > (Bay[(y & 3) * 4 + (x & 3)] + 0.5) / 16.0) lo++;
                    b.SetPixel(x, y, Ramp(st, Math.Min(1.0, lo / (double)levels)));
                }
        }
        static void Rc(Graphics g, Color c, int x, int y, int w, int h)
        {
            using (SolidBrush s = new SolidBrush(c)) g.FillRectangle(s, x, y, w, h);
        }
        static Graphics PxG(Bitmap b)
        {
            Graphics g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            return g;
        }
        static void Disc(Graphics g, int cx, int cy, int r, Color top, Color bot, int cutFrom, int cutStep)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                if (cutStep > 0 && dy > cutFrom && ((dy - cutFrom) % 8) < ((dy - cutFrom) / cutStep + 1)) continue;
                int dx = (int)Math.Sqrt(r * r - dy * dy + 0.0);
                Rc(g, Lp(top, bot, (dy + r) / (2.0 * r)), cx - dx, cy + dy, dx * 2 + 1, 1);
            }
        }
        // linha "pixelada" (sem suavizacao)
        static void PLine(Graphics g, Color c, int x0, int y0, int x1, int y1, int th)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            using (SolidBrush s = new SolidBrush(c))
                while (true)
                {
                    g.FillRectangle(s, x0, y0, th, th);
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 > -dy) { err -= dy; x0 += sx; }
                    if (e2 < dx) { err += dx; y0 += sy; }
                }
        }
        static void PalmPx(Graphics g, int bx, int by, int h, double lean, Color trunk, Color ring, Color leaf, Color leafHi)
        {
            int cx = bx, cy = by;
            for (int i = 0; i <= h; i++)
            {
                double u = i / (double)h;
                cx = bx + (int)(lean * u * u * h * 0.5);
                cy = by - i;
                int w = i < h / 5 ? 4 : 3;
                Rc(g, i % 6 == 0 ? ring : trunk, cx - w / 2, cy, w, 1);
            }
            double[] ang = { 8, 38, 66, 96, 126, 154, 178, -14, 200 };
            double L = h * 0.46;
            for (int k = 0; k < ang.Length; k++)
            {
                double a = ang[k] * Math.PI / 180;
                for (int s = 0; s <= (int)L; s++)
                {
                    double t = s / L;
                    int x = cx + (int)(Math.Cos(a) * L * t);
                    int y = cy - (int)(Math.Sin(a) * L * t) + (int)(L * 0.75 * t * t);
                    Rc(g, leaf, x, y, 2, 2);
                    if (s % 3 == 0 && s > 3)
                    {
                        int d = t < 0.6 ? 3 : 2;
                        Rc(g, leaf, x - d, y + d, 1, 2);
                        Rc(g, leaf, x + d, y + d, 1, 2);
                        Rc(g, leafHi, x, y - 1, 1, 1);
                    }
                }
            }
            Rc(g, leaf, cx - 2, cy - 1, 5, 4);
            Rc(g, ring, cx - 2, cy + 3, 2, 2); Rc(g, ring, cx + 1, cy + 3, 2, 2);
        }

        static Bitmap MakeNeon()
        {
            const int W = 360, H = 250, HZ = 168;
            Bitmap b = new Bitmap(W, H);
            Random r = new Random(7);
            Sky(b, 0, 150, new Color[] { Hex(0x07021a), Hex(0x1a0838), Hex(0x4a0f6e), Hex(0xb0188a), Hex(0xff3d9a) }, 14);
            Color[] wc = { Hex(0x2ef9ff), Hex(0xffd23f), Hex(0xff3da8), Hex(0x9d5cff) };
            using (Graphics g = PxG(b))
            {
                Rc(g, Hex(0xff3d9a), 0, 150, W, HZ - 150);
                for (int i = 0; i < 90; i++)
                {
                    int x = r.Next(W), y = r.Next(120);
                    Color c = r.Next(3) == 0 ? Hex(0x7df9ff) : Color.White;
                    Rc(g, A(255 - y * 2, c.R, c.G, c.B), x, y, 1, 1);
                    if (i % 10 == 0) { Rc(g, A(180, c.R, c.G, c.B), x - 1, y, 3, 1); Rc(g, A(180, c.R, c.G, c.B), x, y - 1, 1, 3); }
                }
                // sol de neon com listras
                Disc(g, 252, 120, 46, Hex(0xffe45c), Hex(0xff2e93), 6, 11);
                // predios ao fundo
                int xx = 0;
                while (xx < W)
                {
                    int w = r.Next(9, 22), h = r.Next(25, 80);
                    Rc(g, Hex(0x2a0b4e), xx, 152 - h, w, h);
                    Rc(g, Hex(0x3e1272), xx, 152 - h, w, 1);
                    for (int wy = 152 - h + 3; wy < 148; wy += 4)
                        for (int wx = xx + 2; wx < xx + w - 2; wx += 4)
                            if (r.Next(5) == 0) Rc(g, Hex(0x8a2aa8), wx, wy, 1, 2);
                    xx += w - (r.Next(3) == 0 ? 3 : 0);
                }
                // predios da frente com janelas e letreiros
                xx = -4;
                while (xx < W)
                {
                    int w = r.Next(14, 34), h = r.Next(50, 118), top = HZ - h;
                    Rc(g, Hex(0x0e0620), xx, top, w, h);
                    Rc(g, Hex(0x2b1257), xx, top, 1, h);
                    Rc(g, Hex(0x1c0c3a), xx, top, w, 1);
                    if (r.Next(3) == 0)
                    {
                        Rc(g, Hex(0x0e0620), xx + w / 2, top - 10, 1, 10);
                        Rc(g, Hex(0xff2040), xx + w / 2, top - 11, 1, 1);
                    }
                    for (int wy = top + 4; wy < HZ - 4; wy += 4)
                        for (int wx = xx + 3; wx < xx + w - 3; wx += 3)
                            if (r.Next(4) == 0) Rc(g, wc[r.Next(4)], wx, wy, 2, 2);
                    if (w >= 20 && r.Next(2) == 0)
                    {
                        Color sc = wc[r.Next(3)];
                        int sx = xx + w - 7, sy = top + 8;
                        Rc(g, A(55, sc.R, sc.G, sc.B), sx - 2, sy - 2, 9, 26);
                        Rc(g, sc, sx, sy, 5, 1); Rc(g, sc, sx, sy + 21, 5, 1); Rc(g, sc, sx, sy, 1, 22); Rc(g, sc, sx + 4, sy, 1, 22);
                        for (int q = 3; q < 20; q += 4) Rc(g, Color.White, sx + 2, sy + q, 1, 2);
                    }
                    xx += w + r.Next(0, 4);
                }
                // rua molhada
                Rc(g, Hex(0x12052a), 0, HZ, W, H - HZ);
                for (int y = HZ + 2; y < H; y += 3)
                {
                    int x1 = r.Next(W), len = r.Next(14, 70);
                    Color c = wc[r.Next(3)];
                    Rc(g, A(70, c.R, c.G, c.B), x1, y, len, 1);
                }
                using (Pen p = new Pen(HexA(190, 0xff2e93), 1f))
                {
                    for (int k = -16; k <= 16; k++) g.DrawLine(p, 180 + k * 3, HZ, 180 + k * 46, H);
                }
                using (Pen p = new Pen(HexA(170, 0x2ef9ff), 1f))
                {
                    for (int i = 1; i <= 9; i++)
                    {
                        int y = HZ + (int)(Math.Pow(i / 9.0, 2) * (H - HZ - 1));
                        g.DrawLine(p, 0, y, W, y);
                    }
                }
                // chuva
                for (int i = 0; i < 140; i++)
                {
                    int x = r.Next(W), y = r.Next(H - 8);
                    Rc(g, HexA(90, 0x9fe8ff), x, y, 1, 2);
                    Rc(g, HexA(60, 0x9fe8ff), x - 1, y + 2, 1, 2);
                }
            }
            return b;
        }

        static Bitmap MakeVice()
        {
            const int W = 360, H = 250, HZ = 142;
            Bitmap b = new Bitmap(W, H);
            Random r = new Random(11);
            Sky(b, 0, HZ, new Color[] { Hex(0x1a0b3d), Hex(0x5a1b7a), Hex(0xc4357e), Hex(0xff6a5c), Hex(0xffb347), Hex(0xffe29a) }, 18);
            Sky(b, HZ, 192, new Color[] { Hex(0xff8a6b), Hex(0xd23a86), Hex(0x6b1b8c), Hex(0x2a0f5c) }, 12);
            using (Graphics g = PxG(b))
            {
                for (int i = 0; i < 40; i++) { int x = r.Next(W), y = r.Next(40); Rc(g, A(230 - y * 4, 255, 255, 255), x, y, 1, 1); }
                // sol
                Disc(g, 180, 126, 48, Hex(0xfff3a0), Hex(0xff5e7e), 4, 9);
                Rc(g, Hex(0xffb347), 0, HZ - 1, W, 1);
                // nuvens em barras
                int[][] cl = { new int[] { 30, 60, 52, 3 }, new int[] { 260, 48, 60, 3 }, new int[] { 70, 84, 38, 2 }, new int[] { 230, 92, 46, 2 }, new int[] { 140, 40, 34, 2 }, new int[] { 300, 100, 40, 2 } };
                foreach (int[] q in cl)
                {
                    Rc(g, HexA(210, 0xff8fb8), q[0], q[1], q[2], q[3]);
                    Rc(g, HexA(210, 0xffd0e0), q[0] + 3, q[1] - 1, q[2] - 8, 1);
                    Rc(g, HexA(160, 0x5a1b7a), q[0] + 6, q[1] + q[3], q[2] - 10, 1);
                }
                // skyline art deco ao fundo
                Color[] pa = { Hex(0xff8fb8), Hex(0x4ee3d0), Hex(0xc9a0ff), Hex(0xffc0a0) };
                int xx = 4;
                while (xx < W - 8)
                {
                    int w = r.Next(10, 20), h = r.Next(14, 46);
                    Color c = Lp(pa[r.Next(4)], Hex(0x8a3a8a), 0.45);
                    Rc(g, c, xx, HZ - h, w, h);
                    Rc(g, Lp(c, Color.White, 0.35), xx, HZ - h, w, 1);
                    Rc(g, Lp(c, Color.Black, 0.3), xx + w - 2, HZ - h, 2, h);
                    if (r.Next(3) == 0) { Rc(g, c, xx + 2, HZ - h - 4, w - 4, 4); Rc(g, Lp(c, Color.White, 0.3), xx + 2, HZ - h - 4, w - 4, 1); }
                    for (int wy = HZ - h + 3; wy < HZ - 2; wy += 3)
                        for (int wx = xx + 2; wx < xx + w - 3; wx += 3)
                            if (r.Next(3) == 0) Rc(g, HexA(200, 0xfff3a0), wx, wy, 1, 1);
                    if (r.Next(6) == 0) { Rc(g, c, xx + w / 2, HZ - h - 14, 1, 14); }
                    xx += w + r.Next(0, 3);
                }
                // reflexo do sol no mar
                for (int y = HZ + 1; y < 190; y += 2)
                {
                    double t = (y - HZ) / 48.0;
                    int w = (int)(70 * (1 - t * 0.6)) + r.Next(-6, 7);
                    int off = r.Next(-3, 4);
                    Rc(g, A((int)(230 - t * 90), 255, 226, 150), 180 - w / 2 + off, y, Math.Max(4, w), 1);
                }
                for (int y = HZ + 4; y < 190; y += 5)
                    Rc(g, HexA(120, 0xffb0c8), r.Next(W), y, r.Next(6, 22), 1);
                // faixa de areia e rua
                Rc(g, Hex(0xf4b8a0), 0, 192, W, 6);
                Rc(g, Hex(0xd88a8a), 0, 196, W, 2);
                Rc(g, Hex(0x2a1546), 0, 198, W, H - 198);
                Rc(g, Hex(0xff3da8), 0, 198, W, 1);
                Rc(g, Hex(0x2ef9ff), 0, 199, W, 1);
                for (int x = 6; x < W; x += 34) Rc(g, Hex(0xfff3a0), x, 226, 18, 2);
                for (int x = 0; x < W; x += 5) Rc(g, Hex(0x3a1c60), x + (x / 5) % 2 * 2, 236 + (x / 5) % 3 * 3, 3, 1);
                // palmeiras (silhuetas)
                Color tr = Hex(0x1c0a33), rg = Hex(0x321452), lf = Hex(0x14072a), lh = Hex(0x4b1f6e);
                PalmPx(g, 38, 214, 132, -0.35, tr, rg, lf, lh);
                PalmPx(g, 96, 210, 84, 0.3, tr, rg, lf, lh);
                PalmPx(g, 324, 216, 150, 0.4, tr, rg, lf, lh);
                PalmPx(g, 276, 208, 78, -0.25, tr, rg, lf, lh);
                // conversivel na orla
                int cx = 196, cy = 212;
                Rc(g, HexA(110, 0x2ef9ff), cx - 2, cy + 11, 66, 2);
                Rc(g, Hex(0x1a0b2e), cx, cy + 3, 62, 8);
                Rc(g, Hex(0x1a0b2e), cx + 4, cy - 1, 18, 4); Rc(g, Hex(0x1a0b2e), cx + 20, cy - 3, 14, 6);
                Rc(g, Hex(0xff3da8), cx, cy + 6, 62, 1);
                Rc(g, Hex(0x7df9ff), cx + 22, cy - 2, 9, 3);
                Rc(g, Hex(0xff2040), cx - 1, cy + 4, 2, 2); Rc(g, Hex(0xfff3a0), cx + 61, cy + 5, 2, 2);
                foreach (int wx in new int[] { cx + 10, cx + 48 })
                {
                    Rc(g, Hex(0x0a0418), wx - 4, cy + 7, 9, 7); Rc(g, Hex(0x0a0418), wx - 3, cy + 6, 7, 9);
                    Rc(g, Hex(0x2ef9ff), wx - 1, cy + 9, 3, 3);
                }
            }
            return b;
        }

        // ================= Steampunk (arte realista, estatica) =================
        static Color Cu(double f)
        {
            return Ramp(new Color[] { Hex(0x3a1c08), Hex(0x8a4a1c), Hex(0xd98a4a), Hex(0xf6c79a), Hex(0xb5601f), Hex(0x5a2c0e), Hex(0x2a1306) }, f);
        }
        static void PipeLine(Graphics g, float x1, float y1, float x2, float y2, float th)
        {
            double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.5) return;
            float nx = (float)(-dy / len), ny = (float)(dx / len);
            int n = Math.Max(2, (int)th);
            for (int i = 0; i <= n; i++)
            {
                float o = i - th / 2f;
                using (Pen p = new Pen(Cu(i / (double)n), 1.7f)) g.DrawLine(p, x1 + nx * o, y1 + ny * o, x2 + nx * o, y2 + ny * o);
            }
        }
        static void PipeArc(Graphics g, float cx, float cy, float r, float a0, float sweep, float th)
        {
            int n = Math.Max(2, (int)th);
            for (int i = 0; i <= n; i++)
            {
                float rr = r + i - th / 2f;
                using (Pen p = new Pen(Cu(i / (double)n), 1.7f)) g.DrawArc(p, cx - rr, cy - rr, 2 * rr, 2 * rr, a0, sweep);
            }
        }
        static void Bolt(Graphics g, float x, float y, float r)
        {
            using (SolidBrush d = new SolidBrush(A(200, 30, 16, 4))) g.FillEllipse(d, x - r - 0.6f, y - r - 0.3f, 2 * r + 1.4f, 2 * r + 1.4f);
            RectangleF rc = new RectangleF(x - r, y - r, 2 * r, 2 * r);
            using (LinearGradientBrush lb = new LinearGradientBrush(rc, Hex(0xf5da92), Hex(0x6a4510), 45f)) g.FillEllipse(lb, rc);
            using (SolidBrush h = new SolidBrush(A(200, 255, 245, 210))) g.FillEllipse(h, x - r * 0.5f, y - r * 0.55f, r * 0.5f, r * 0.4f);
        }
        static void Collar(Graphics g, float cx, float cy, float len, float th, bool horiz)
        {
            float w = horiz ? len : th + 10, h = horiz ? th + 10 : len;
            RectangleF rc = new RectangleF(cx - w / 2, cy - h / 2, w, h);
            using (SolidBrush sh = new SolidBrush(A(90, 0, 0, 0))) g.FillRectangle(sh, rc.X + 3, rc.Y + 4, rc.Width, rc.Height);
            using (LinearGradientBrush lb = new LinearGradientBrush(rc, Hex(0xf1d487), Hex(0x6e4a12), horiz ? 90f : 0f))
            {
                ColorBlend cb = new ColorBlend(5);
                cb.Positions = new float[] { 0f, 0.2f, 0.45f, 0.75f, 1f };
                cb.Colors = new Color[] { Hex(0x7a5416), Hex(0xe9c46c), Hex(0xfbe9b0), Hex(0xb98a2a), Hex(0x4e330a) };
                lb.InterpolationColors = cb;
                g.FillRectangle(lb, rc);
            }
            using (Pen e = new Pen(A(230, 40, 22, 6), 1.2f)) g.DrawRectangle(e, rc.X, rc.Y, rc.Width, rc.Height);
            if (horiz) { Bolt(g, cx, rc.Y + 4, 2.4f); Bolt(g, cx, rc.Bottom - 4, 2.4f); }
            else { Bolt(g, rc.X + 4, cy, 2.4f); Bolt(g, rc.Right - 4, cy, 2.4f); }
        }
        static void Joint(Graphics g, float cx, float cy, float r)
        {
            using (SolidBrush sh = new SolidBrush(A(90, 0, 0, 0))) g.FillEllipse(sh, cx - r + 3, cy - r + 4, 2 * r, 2 * r);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = Hex(0xfbe9b0); pb.SurroundColors = new Color[] { Hex(0x5a3a0c) };
                    pb.CenterPoint = new PointF(cx - r * 0.35f, cy - r * 0.4f);
                    g.FillPath(pb, gp);
                }
            }
            using (Pen e = new Pen(A(220, 40, 22, 6), 1.2f)) g.DrawEllipse(e, cx - r, cy - r, 2 * r, 2 * r);
            for (int k = 0; k < 6; k++) Bolt(g, cx + (float)Math.Cos(k * Math.PI / 3) * r * 0.62f, cy + (float)Math.Sin(k * Math.PI / 3) * r * 0.62f, 1.8f);
        }
        static PointF[] GearPts(float cx, float cy, float r, int n, double ang)
        {
            PointF[] pts = new PointF[n * 4];
            double w = 2 * Math.PI / n;
            for (int i = 0; i < n; i++)
            {
                double a0 = ang + i * w;
                double[] aa = { a0, a0 + w * 0.16, a0 + w * 0.40, a0 + w * 0.56 };
                float[] rr = { r * 0.84f, r, r, r * 0.84f };
                for (int k = 0; k < 4; k++) pts[i * 4 + k] = new PointF(cx + (float)(Math.Cos(aa[k]) * rr[k]), cy + (float)(Math.Sin(aa[k]) * rr[k]));
            }
            return pts;
        }
        static void GearR(Graphics g, float cx, float cy, float r, int n, double rot, Color lite, Color dark)
        {
            PointF[] pts = GearPts(cx, cy, r, n, rot);
            PointF[] sh = new PointF[pts.Length];
            for (int i = 0; i < pts.Length; i++) sh[i] = new PointF(pts[i].X + r * 0.07f, pts[i].Y + r * 0.1f);
            using (SolidBrush sb = new SolidBrush(A(100, 0, 0, 0))) g.FillPolygon(sb, sh);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddPolygon(pts);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = lite; pb.SurroundColors = new Color[] { dark };
                    pb.CenterPoint = new PointF(cx - r * 0.3f, cy - r * 0.3f);
                    g.FillPath(pb, gp);
                }
            }
            using (Pen e = new Pen(A(230, 38, 20, 6), Math.Max(1.2f, r * 0.02f))) g.DrawPolygon(e, pts);
            float ri = r * 0.72f, rh = r * 0.2f;
            // vazados entre os raios
            using (SolidBrush hole = new SolidBrush(A(200, 22, 12, 4))) g.FillEllipse(hole, cx - ri, cy - ri, 2 * ri, 2 * ri);
            using (Pen p = new Pen(A(255, 120, 74, 24), r * 0.07f)) g.DrawEllipse(p, cx - ri, cy - ri, 2 * ri, 2 * ri);
            using (Pen p = new Pen(A(200, 245, 205, 130), 1.2f)) g.DrawEllipse(p, cx - ri - r * 0.03f, cy - ri - r * 0.03f, 2 * ri, 2 * ri);
            int spokes = n >= 10 ? 6 : 5;
            for (int k = 0; k < spokes; k++)
            {
                double a = rot + k * 2 * Math.PI / spokes;
                float x1 = cx + (float)Math.Cos(a) * rh, y1 = cy + (float)Math.Sin(a) * rh, x2 = cx + (float)Math.Cos(a) * ri, y2 = cy + (float)Math.Sin(a) * ri;
                using (Pen p = new Pen(A(255, 70, 40, 12), r * 0.15f)) g.DrawLine(p, x1 + 1.5f, y1 + 2f, x2 + 1.5f, y2 + 2f);
                using (Pen p = new Pen(Lp(dark, lite, 0.55), r * 0.12f)) g.DrawLine(p, x1, y1, x2, y2);
                using (Pen p = new Pen(A(190, 250, 225, 170), Math.Max(1f, r * 0.03f))) g.DrawLine(p, x1 - 1, y1 - 1, x2 - 1, y2 - 1);
            }
            using (GraphicsPath hp = new GraphicsPath())
            {
                hp.AddEllipse(cx - rh, cy - rh, 2 * rh, 2 * rh);
                using (PathGradientBrush pb = new PathGradientBrush(hp))
                {
                    pb.CenterColor = Hex(0xfbe9b0); pb.SurroundColors = new Color[] { Hex(0x6a4510) };
                    pb.CenterPoint = new PointF(cx - rh * 0.35f, cy - rh * 0.4f);
                    g.FillPath(pb, hp);
                }
            }
            using (Pen e = new Pen(A(230, 38, 20, 6), 1.2f)) g.DrawEllipse(e, cx - rh, cy - rh, 2 * rh, 2 * rh);
            Bolt(g, cx, cy, Math.Max(2.4f, rh * 0.38f));
        }
        static void GaugeR(Graphics g, float cx, float cy, float r, double v, string unit, bool nums)
        {
            using (SolidBrush sh = new SolidBrush(A(100, 0, 0, 0))) g.FillEllipse(sh, cx - r + 5, cy - r + 7, 2 * r, 2 * r);
            RectangleF rb = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
            using (LinearGradientBrush lb = new LinearGradientBrush(rb, Hex(0xf6dc96), Hex(0x6b4410), 45f))
            {
                ColorBlend cb = new ColorBlend(4);
                cb.Positions = new float[] { 0f, 0.3f, 0.6f, 1f };
                cb.Colors = new Color[] { Hex(0xfbe9b0), Hex(0xc99a3c), Hex(0x7a5416), Hex(0xd8aa50) };
                lb.InterpolationColors = cb;
                g.FillEllipse(lb, rb);
            }
            using (Pen e = new Pen(A(230, 38, 20, 6), 1.5f)) g.DrawEllipse(e, rb.X, rb.Y, rb.Width, rb.Height);
            float r2 = r * 0.86f, r3 = r * 0.8f;
            using (SolidBrush d = new SolidBrush(Hex(0x2a1708))) g.FillEllipse(d, cx - r2, cy - r2, 2 * r2, 2 * r2);
            using (GraphicsPath fp = new GraphicsPath())
            {
                fp.AddEllipse(cx - r3, cy - r3, 2 * r3, 2 * r3);
                using (PathGradientBrush pb = new PathGradientBrush(fp))
                {
                    pb.CenterColor = Hex(0xf9efd0); pb.SurroundColors = new Color[] { Hex(0xc2a972) };
                    g.FillPath(pb, fp);
                }
            }
            using (Pen rz = new Pen(A(210, 176, 32, 22), r * 0.07f)) g.DrawArc(rz, cx - r * 0.7f, cy - r * 0.7f, r * 1.4f, r * 1.4f, 135 + 27 * 8, 54);
            for (int i = 0; i <= 40; i++)
            {
                double a = (135 + i * 6.75) * Math.PI / 180;
                bool major = i % 4 == 0;
                float r1 = r * 0.76f, rt = r * (major ? 0.62f : 0.69f);
                using (Pen tk = new Pen(A(240, 50, 28, 10), major ? Math.Max(1.4f, r * 0.03f) : 1f))
                    g.DrawLine(tk, cx + (float)(Math.Cos(a) * r1), cy + (float)(Math.Sin(a) * r1), cx + (float)(Math.Cos(a) * rt), cy + (float)(Math.Sin(a) * rt));
            }
            if (nums)
                using (Font f = new Font("Georgia", Math.Max(6f, r * 0.15f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush tb = new SolidBrush(Hex(0x3a200a)))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                    for (int i = 0; i <= 10; i += 2)
                    {
                        double a = (135 + i * 27) * Math.PI / 180;
                        g.DrawString((i * 10).ToString(), f, tb, cx + (float)(Math.Cos(a) * r * 0.48), cy + (float)(Math.Sin(a) * r * 0.48), sf);
                    }
                }
            using (Font f2 = new Font("Georgia", Math.Max(6f, r * 0.12f), FontStyle.Italic, GraphicsUnit.Pixel))
            using (SolidBrush tb2 = new SolidBrush(Hex(0x4a2c10)))
            using (StringFormat sf2 = new StringFormat())
            {
                sf2.Alignment = StringAlignment.Center; sf2.LineAlignment = StringAlignment.Center;
                g.DrawString(unit, f2, tb2, cx, cy + r * 0.3f, sf2);
            }
            double na = (135 + v * 270) * Math.PI / 180;
            float cs = (float)Math.Cos(na), sn = (float)Math.Sin(na);
            PointF tip = new PointF(cx + cs * r * 0.7f, cy + sn * r * 0.7f), tail = new PointF(cx - cs * r * 0.2f, cy - sn * r * 0.2f);
            PointF b1 = new PointF(cx - sn * r * 0.05f, cy + cs * r * 0.05f), b2 = new PointF(cx + sn * r * 0.05f, cy - cs * r * 0.05f);
            using (SolidBrush ns = new SolidBrush(A(110, 0, 0, 0))) g.FillPolygon(ns, new PointF[] { new PointF(tip.X + 2, tip.Y + 3), new PointF(b1.X + 2, b1.Y + 3), new PointF(tail.X + 2, tail.Y + 3), new PointF(b2.X + 2, b2.Y + 3) });
            using (SolidBrush nb = new SolidBrush(Hex(0x8a1a10))) g.FillPolygon(nb, new PointF[] { tip, b1, tail, b2 });
            using (GraphicsPath hp = new GraphicsPath())
            {
                float hr = r * 0.09f;
                hp.AddEllipse(cx - hr, cy - hr, 2 * hr, 2 * hr);
                using (PathGradientBrush pb = new PathGradientBrush(hp)) { pb.CenterColor = Hex(0xfbe9b0); pb.SurroundColors = new Color[] { Hex(0x6a4510) }; g.FillPath(pb, hp); }
            }
            using (LinearGradientBrush gl = new LinearGradientBrush(new RectangleF(cx - r * 0.72f, cy - r * 0.8f, r * 1.1f, r * 0.9f), A(120, 255, 255, 255), A(0, 255, 255, 255), 90f))
                g.FillEllipse(gl, cx - r * 0.72f, cy - r * 0.8f, r * 1.1f, r * 0.9f);
            for (int k = 0; k < 4; k++)
            {
                double a = (45 + 90 * k) * Math.PI / 180;
                Bolt(g, cx + (float)Math.Cos(a) * r * 0.93f, cy + (float)Math.Sin(a) * r * 0.93f, Math.Max(1.6f, r * 0.045f));
            }
        }
        static void Valve(Graphics g, float cx, float cy, float r)
        {
            using (SolidBrush sh = new SolidBrush(A(90, 0, 0, 0))) g.FillEllipse(sh, cx - r + 4, cy - r + 6, 2 * r, 2 * r);
            for (int k = 0; k < 3; k++)
            {
                double a = k * Math.PI / 3;
                float dx = (float)Math.Cos(a) * r, dy = (float)Math.Sin(a) * r;
                using (Pen p = new Pen(Hex(0x6a1610), r * 0.14f)) g.DrawLine(p, cx - dx, cy - dy, cx + dx, cy + dy);
                using (Pen p = new Pen(Hex(0xc03a28), r * 0.06f)) g.DrawLine(p, cx - dx - 1, cy - dy - 1, cx + dx - 1, cy + dy - 1);
            }
            using (Pen p = new Pen(Hex(0x6a1610), r * 0.17f)) g.DrawEllipse(p, cx - r, cy - r, 2 * r, 2 * r);
            using (Pen p = new Pen(Hex(0xd24a34), r * 0.07f)) g.DrawEllipse(p, cx - r - 1, cy - r - 1, 2 * r, 2 * r);
            Bolt(g, cx, cy, r * 0.16f);
        }
        static void Puff(Graphics g, float cx, float cy, float r, int a)
        {
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = A(a, 255, 250, 240); pb.SurroundColors = new Color[] { A(0, 255, 250, 240) };
                    g.FillPath(pb, gp);
                }
            }
        }

        // tubos usados como divisorias dos menus (tema Steampunk)
        public static void HPipe(Graphics g, float x1, float x2, float y, float th)
        {
            PipeLine(g, x1, y, x2, y, th);
            Collar(g, x1 + 3, y, 7, th, false);
            Collar(g, x2 - 3, y, 7, th, false);
            Collar(g, (x1 + x2) / 2, y, 8, th, false);
        }
        public static void VPipe(Graphics g, float x, float y1, float y2, float th)
        {
            PipeLine(g, x, y1, x, y2, th);
            for (float y = y1 + 40; y < y2 - 10; y += 150) Collar(g, x, y, 7, th, true);
        }
        public static void GroupPipe(Graphics g, int w, int h)
        {
            using (GraphicsPath p = Xp.Round(new Rectangle(2, 10, w - 5, h - 13), 5))
            {
                using (Pen d = new Pen(A(255, 52, 26, 8), 5f)) g.DrawPath(d, p);
                using (Pen m = new Pen(Hex(0xb5601f), 3.2f)) g.DrawPath(m, p);
                g.TranslateTransform(-0.8f, -0.8f);
                using (Pen l = new Pen(A(210, 246, 199, 154), 1f)) g.DrawPath(l, p);
                g.TranslateTransform(0.8f, 0.8f);
            }
            Bolt(g, 3, 11, 2.6f); Bolt(g, w - 3, 11, 2.6f); Bolt(g, 3, h - 4, 2.6f); Bolt(g, w - 3, h - 4, 2.6f);
        }

        static Bitmap MakeSteam()
        {
            const int W = 1000, H = 700;
            Bitmap b = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
            Random r = new Random(5);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // parede de madeira escura com rebites
                using (LinearGradientBrush wall = new LinearGradientBrush(new Rectangle(0, 0, W, H), Hex(0x3b2515), Hex(0x1e1209), 90f)) g.FillRectangle(wall, 0, 0, W, H);
                for (int i = 0; i < 700; i++)
                {
                    int x = r.Next(W), y = r.Next(H), l = r.Next(20, 120);
                    using (Pen p = new Pen(A(r.Next(8, 22), r.Next(2) == 0 ? 0 : 255, r.Next(2) == 0 ? 0 : 200, 120), 1f)) g.DrawLine(p, x, y, x + l, y);
                }
                for (int x = 0; x < W; x += 96)
                {
                    using (Pen p = new Pen(A(120, 0, 0, 0), 3f)) g.DrawLine(p, x, 0, x, H);
                    using (Pen p = new Pen(A(40, 255, 210, 150), 1f)) g.DrawLine(p, x + 2, 0, x + 2, H);
                    for (int y = 40; y < H; y += 120) Bolt(g, x + 48, y, 3.2f);
                }
                Color gl = Hex(0xd7a75a), gd = Hex(0x4a2c0c);
                // engrenagens de fundo
                GearR(g, 90, 112, 78, 14, 0.2, gl, gd);
                GearR(g, 210, 190, 50, 10, 0.55, gl, gd);
                GearR(g, 862, 98, 70, 13, 0.1, gl, gd);
                GearR(g, 936, 212, 42, 9, 0.4, gl, gd);
                GearR(g, 60, 546, 66, 12, 0.3, gl, gd);
                GearR(g, 160, 630, 40, 9, 0.7, gl, gd);
                GearR(g, 500, 650, 56, 11, 0.15, gl, gd);
                // tubulacao
                PipeLine(g, -10, 49, W + 10, 49, 16);
                for (int x = 70; x < W; x += 230) Collar(g, x, 49, 12, 16, false);
                PipeLine(g, -10, 294, W + 10, 294, 28);
                for (int x = 60; x < W; x += 210) Collar(g, x, 294, 14, 28, false);
                PipeLine(g, 620, 294, 620, H + 10, 24);
                Collar(g, 620, 430, 12, 24, true); Collar(g, 620, 575, 12, 24, true);
                PipeLine(g, 140, -10, 140, 294, 20);
                Collar(g, 140, 120, 12, 20, true); Collar(g, 140, 220, 12, 20, true);
                Joint(g, 140, 294, 24); Joint(g, 620, 294, 26);
                PipeLine(g, -10, 640, 380, 640, 18);
                PipeArc(g, 380, 600, 40, 0, 90, 18);
                PipeLine(g, 420, 600, 420, 470, 18);
                Collar(g, 420, 470, 12, 18, true); Collar(g, 120, 640, 12, 18, false); Collar(g, 260, 640, 12, 18, false);
                // verdigris nas juntas
                for (int i = 0; i < 70; i++)
                {
                    float x = 40 + r.Next(W - 80), y = i % 2 == 0 ? 294 + r.Next(-12, 13) : 49 + r.Next(-6, 7);
                    using (SolidBrush v = new SolidBrush(A(r.Next(25, 55), 70, 170, 140))) g.FillEllipse(v, x, y, r.Next(3, 9), r.Next(2, 5));
                }
                // manometros ligados por derivacoes
                PipeLine(g, 720, 300, 720, 356, 10); GaugeR(g, 720, 408, 52, 0.62, "PSI", true);
                PipeLine(g, 300, 640, 300, 566, 10); GaugeR(g, 300, 520, 44, 0.35, "BAR", true);
                PipeLine(g, 100, 300, 100, 330, 9); GaugeR(g, 100, 366, 36, 0.78, "TEMP", false);
                PipeLine(g, 500, 56, 500, 66, 9); GaugeR(g, 500, 108, 42, 0.5, "VAC", true);
                // valvula com vapor
                PipeLine(g, 860, 290, 860, 252, 8); Valve(g, 860, 232, 30);
                Puff(g, 905, 200, 30, 70); Puff(g, 925, 160, 42, 55); Puff(g, 945, 112, 56, 40); Puff(g, 960, 56, 70, 28);
                PipeLine(g, 380, 56, 380, 80, 8); Valve(g, 380, 104, 22);
                // engrenagens da frente
                GearR(g, 880, 590, 104, 16, 0.05, gl, gd);
                GearR(g, 745, 660, 46, 10, 0.5, gl, gd);
                // vinheta
                using (GraphicsPath vp = new GraphicsPath())
                {
                    vp.AddRectangle(new Rectangle(0, 0, W, H));
                    using (PathGradientBrush pb = new PathGradientBrush(vp))
                    {
                        pb.CenterColor = A(0, 0, 0, 0); pb.SurroundColors = new Color[] { A(150, 0, 0, 0) };
                        pb.FocusScales = new PointF(0.6f, 0.6f);
                        g.FillRectangle(pb, 0, 0, W, H);
                    }
                }
            }
            return b;
        }
    }

    // ------------------------------------------------------------------
    //  Render OpenGL real (carga da GPU): pelos em um toro, estilo FurMark
    // ------------------------------------------------------------------
    class GlCanvas : Control
    {
        [StructLayout(LayoutKind.Sequential)]
        struct PFD
        {
            public ushort nSize, nVersion; public uint dwFlags;
            public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift, cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits, cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
            public uint dwLayerMask, dwVisibleMask, dwDamageMask;
        }
        [DllImport("gdi32.dll")] static extern int ChoosePixelFormat(IntPtr hdc, ref PFD pfd);
        [DllImport("gdi32.dll")] static extern bool SetPixelFormat(IntPtr hdc, int fmt, ref PFD pfd);
        [DllImport("gdi32.dll")] static extern bool SwapBuffers(IntPtr hdc);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        const string GL = "opengl32.dll";
        [DllImport(GL)] static extern IntPtr wglCreateContext(IntPtr hdc);
        [DllImport(GL)] static extern bool wglMakeCurrent(IntPtr hdc, IntPtr rc);
        [DllImport(GL)] static extern bool wglDeleteContext(IntPtr rc);
        [DllImport(GL)] static extern IntPtr wglGetProcAddress(string n);
        [DllImport(GL)] static extern void glClearColor(float r, float g, float b, float a);
        [DllImport(GL)] static extern void glClear(uint m);
        [DllImport(GL)] static extern void glViewport(int x, int y, int w, int h);
        [DllImport(GL)] static extern void glMatrixMode(uint m);
        [DllImport(GL)] static extern void glLoadIdentity();
        [DllImport(GL)] static extern void glFrustum(double l, double r, double b, double t, double n, double f);
        [DllImport(GL)] static extern void glTranslatef(float x, float y, float z);
        [DllImport(GL)] static extern void glRotatef(float a, float x, float y, float z);
        [DllImport(GL)] static extern void glEnable(uint c);
        [DllImport(GL)] static extern void glDisable(uint c);
        [DllImport(GL)] static extern void glBlendFunc(uint s, uint d);
        [DllImport(GL)] static extern void glLineWidth(float w);
        [DllImport(GL)] static extern void glEnableClientState(uint c);
        [DllImport(GL)] static extern void glDisableClientState(uint c);
        [DllImport(GL)] static extern void glVertexPointer(int size, uint type, int stride, IntPtr p);
        [DllImport(GL)] static extern void glColorPointer(int size, uint type, int stride, IntPtr p);
        [DllImport(GL)] static extern void glDrawArrays(uint mode, int first, int count);
        [DllImport(GL)] static extern void glBegin(uint m);
        [DllImport(GL)] static extern void glEnd();
        [DllImport(GL)] static extern void glVertex2f(float x, float y);
        [DllImport(GL)] static extern IntPtr glGetString(uint n);
        [DllImport(GL)] static extern void glPushMatrix();
        [DllImport(GL)] static extern void glPopMatrix();
        [DllImport(GL)] static extern void glVertex3f(float x, float y, float z);
        [DllImport(GL)] static extern void glDepthMask(byte f);
        [DllImport(GL)] static extern void glColor4f(float r, float g, float b, float a);
        [DllImport(GL)] static extern void glTexCoord2f(float u, float v);
        [DllImport(GL)] static extern void glGenTextures(int n, out uint t);
        [DllImport(GL)] static extern void glBindTexture(uint target, uint t);
        [DllImport(GL)] static extern void glTexParameteri(uint target, uint pn, int v);
        [DllImport(GL)] static extern void glTexImage2D(uint target, int level, int ifmt, int w, int h, int border, uint fmt, uint type, IntPtr px);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint DCreateShader(uint type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DShaderSource(uint s, int count, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] src, IntPtr len);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DCompile(uint s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DGetiv(uint s, uint pname, out int p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint DCreateProgram();
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DAttach(uint p, uint s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DLink(uint p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DUse(uint p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DGetUniform(uint p, [MarshalAs(UnmanagedType.LPStr)] string name);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DUniform1f(int loc, float v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DUniform2f(int loc, float a, float b);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DSwapInterval(int i);

        public volatile int State; // 0 iniciando, 1 ok, 2 falhou
        public string Error = "", Renderer = "", GlVersion = "", GlVendor = "";
        public BenchCanvas Hud; public string LoadTxt = "";
        volatile bool quit;
        public void StopRender() { quit = true; } volatile int vw = 640, vh = 400;
        Th.Thread th;

        public GlCanvas()
        {
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Black;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnResize(EventArgs e) { base.OnResize(e); vw = Math.Max(1, Width); vh = Math.Max(1, Height); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (th != null) return;
            vw = Math.Max(1, Width); vh = Math.Max(1, Height);
            IntPtr h = Handle;
            th = new Th.Thread(delegate() { Run(h); });
            th.IsBackground = true; th.Name = "GL"; th.Start();
        }
        protected override void Dispose(bool d)
        {
            quit = true;
            if (th != null && th.IsAlive) th.Join(3000);
            base.Dispose(d);
        }

        T Fn<T>(string name) where T : class
        {
            IntPtr p = wglGetProcAddress(name);
            if (p == IntPtr.Zero || p == (IntPtr)1 || p == (IntPtr)2 || p == (IntPtr)3 || p == (IntPtr)(-1)) return null;
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        // fundo: teia polar + laco pesado de ALU (carga fixa e comparavel entre placas; o resultado entra na cor, entao nao pode ser descartado)
        const string FS =
            "uniform float t; uniform vec2 res; uniform float it;\n" +
            "void main(){ vec2 p=(gl_FragCoord.xy-0.5*res)/res.y; vec2 q=p*3.0; float a=0.0;\n" +
            "for(int i=0;i<6000;i++){ if(float(i)>=it) break; q=vec2(sin(q.y*1.7+t)+q.x*0.3, cos(q.x*1.3-t)-q.y*0.3); a+=length(q)*0.01; }\n" +
            "float r=length(p); float ang=atan(p.y,p.x);\n" +
            "float rl=abs(fract(r*6.0)-0.5); float ring=smoothstep(0.47,0.5,rl);\n" +
            "float sa=abs(fract(ang*3.8197186)-0.5); float spoke=smoothstep(0.46,0.5,sa);\n" +
            "float fade=1.0-smoothstep(0.25,1.3,r)*0.7;\n" +
            "vec3 c=vec3(0.03,0.04,0.06)+vec3(0.10,0.13,0.20)*max(ring,spoke)*fade+vec3(a*0.0001);\n" +
            "gl_FragColor=vec4(c,1.0); }";

        // ---- HUD desenhado sobre o render (texto GDI+ em textura) ----
        Bitmap hudBmp; byte[] hudPx; GCHandle hudPin; uint hudTex; bool hudOk = true; int hudW, hudH, hudLW = 640, hudLH = 480, hudPW = 640, hudPH = 480;
        DateTime hudT = DateTime.MinValue, frT = DateTime.UtcNow, tHistT = DateTime.MinValue; int frCnt; double fpsNow;
        List<double> tHist = new List<double>();
        static readonly Color Gold = Color.FromArgb(255, 196, 40), Orange = Color.FromArgb(255, 140, 20);

        void HudRender(int w, int h, string rend)
        {
            hudPW = Math.Max(320, Math.Min(2048, w)); hudPH = Math.Max(240, Math.Min(1024, h));
            float kk = hudPW / 640f;
            hudLW = hudPW; hudLH = hudPH;
            int LW = 640, LH = (int)(hudPH / kk);
            BenchCanvas hd = Hud;
            double tmp = hd != null ? hd.Temp : double.NaN;
            if ((DateTime.UtcNow - tHistT).TotalSeconds >= 1)
            {
                tHistT = DateTime.UtcNow;
                if (!double.IsNaN(tmp)) { tHist.Add(tmp); if (tHist.Count > 60) tHist.RemoveAt(0); }
            }
            string ts = double.IsNaN(tmp) ? "--" : tmp.ToString("0");
            int es = hd != null ? (int)hd.Elapsed : 0, tot = hd != null ? (int)hd.Total : 0;
            string tm2 = (es / 60).ToString("00") + ":" + (es % 60).ToString("00") + (tot > 0 ? " / " + (tot / 60).ToString("00") + ":" + (tot % 60).ToString("00") : "");
            long errs = hd != null ? hd.Errors : 0;
            double avgR = hd != null ? hd.Rate : 0;
            using (Graphics g = Graphics.FromImage(hudBmp))
            using (Font fT = new Font("Consolas", 11f, FontStyle.Bold), fS = new Font("Consolas", 8.5f, FontStyle.Bold), fBig = new Font("Consolas", 26f, FontStyle.Bold), fMid = new Font("Consolas", 13f, FontStyle.Bold), fGold = new Font("Consolas", 21f, FontStyle.Bold))
            using (StringFormat far = new StringFormat(StringFormatFlags.NoWrap))
            using (Pen pn = new Pen(Color.FromArgb(200, 200, 200)), pr = new Pen(Color.FromArgb(235, 40, 40)))
            {
                far.Alignment = StringAlignment.Far;
                g.Clear(Color.Transparent);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(kk, kk);
                Color grey = Color.FromArgb(170, 170, 170);
                using (SolidBrush bGold = new SolidBrush(Gold), bOr = new SolidBrush(Orange), bW = new SolidBrush(Color.White), bG = new SolidBrush(grey), bR = new SolidBrush(Color.FromArgb(235, 40, 40)))
                {
                    // esquerda, topo
                    g.DrawString("FURBALL BENCHMARK " + Xp.Version, fT, bOr, 8, 6);
                    g.DrawString(w + "x" + h, fT, bW, 8, 24);
                    string gn = string.IsNullOrEmpty(rend) ? "GPU" : rend;
                    if (gn.Length > 44) gn = gn.Substring(0, 44);
                    g.DrawString("GPU: " + gn, fS, bG, 8, 46);
                    g.DrawString("OpenGL " + GlVersion, fS, bG, 8, 59);
                    g.DrawString(GlVendor, fS, bG, 8, 72);
                    // direita, topo
                    RectangleF rr = new RectangleF(0, 4, LW - 8, 40);
                    g.DrawString(fpsNow.ToString("0") + " FPS", fBig, bW, rr, far);
                    float yy = 44;
                    float vw0 = g.MeasureString(ts + "°C", fMid).Width - 6;
                    g.DrawString("Temp. GPU: ", fMid, bW, new RectangleF(0, yy, LW - 8 - vw0, 22), far);
                    g.DrawString(ts + "°C", fMid, bR, new RectangleF(0, yy, LW - 8, 22), far);
                    // grafico de historico
                    Rectangle gr = new Rectangle(LW - 8 - 170, 70, 170, 56);
                    g.FillRectangle(Brushes.Black, gr);
                    g.DrawRectangle(pn, gr);
                    double mn = 1e9, mx = -1e9;
                    foreach (double v in tHist) { if (v < mn) mn = v; if (v > mx) mx = v; }
                    if (tHist.Count >= 2)
                    {
                        double lo = Math.Min(mn, mx - 10), span = Math.Max(10, mx - lo);
                        Point[] pts = new Point[tHist.Count];
                        for (int i = 0; i < pts.Length; i++)
                            pts[i] = new Point(gr.X + 2 + (int)((gr.Width - 4) * (double)i / 59.0), gr.Bottom - 3 - (int)((gr.Height - 6) * (tHist[i] - lo) / span));
                        g.DrawLines(pr, pts);
                    }
                    string mm = tHist.Count > 0 ? "Mín: " + mn.ToString("0") + "°C   Máx: " + mx.ToString("0") + "°C" : "Temp. GPU: " + ts + "°C";
                    g.DrawString(mm, fS, bR, new RectangleF(0, 130, LW - 8, 16), far);
                    // embaixo, esquerda
                    g.DrawString("THE GOLDEN FURBALL", fGold, bGold, 8, LH - 70);
                    g.DrawString("Tempo: " + tm2 + "    Erros: " + errs, fS, bW, 10, LH - 36);
                    g.DrawString("FPS médio do teste: " + avgR.ToString("0") + "    Carga: " + LoadTxt, fS, bW, 10, LH - 21);
                }
            }
            BitmapData bd = hudBmp.LockBits(new Rectangle(0, 0, 2048, 1024), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] src = new byte[bd.Stride * 1024];
                Marshal.Copy(bd.Scan0, src, 0, src.Length);
                for (int y = 0; y < hudPH; y++)
                {
                    int o = y * 8192;
                    for (int x = 0; x < hudPW; x++)
                    {
                        int i = o + x * 4;
                        hudPx[i] = src[i + 2]; hudPx[i + 1] = src[i + 1]; hudPx[i + 2] = src[i]; hudPx[i + 3] = src[i + 3];
                    }
                }
            }
            finally { hudBmp.UnlockBits(bd); }
        }

        void Run(IntPtr hwnd)
        {
            IntPtr hdc = IntPtr.Zero, rc = IntPtr.Zero;
            GCHandle hv = default(GCHandle), hc = default(GCHandle);
            try
            {
                hdc = GetDC(hwnd);
                PFD pfd = new PFD();
                pfd.nSize = (ushort)Marshal.SizeOf(typeof(PFD)); pfd.nVersion = 1; pfd.dwFlags = 4 | 0x20 | 1; pfd.iPixelType = 0; pfd.cColorBits = 32; pfd.cDepthBits = 24;
                int fmt = ChoosePixelFormat(hdc, ref pfd);
                if (fmt == 0 || !SetPixelFormat(hdc, fmt, ref pfd)) throw new Exception("formato de pixel OpenGL indisponível");
                rc = wglCreateContext(hdc);
                if (rc == IntPtr.Zero || !wglMakeCurrent(hdc, rc)) throw new Exception("não foi possível criar o contexto OpenGL");
                string rend = "";
                try { rend = Marshal.PtrToStringAnsi(glGetString(0x1F01)); } catch (Exception) { }
                DSwapInterval swap = null;
                try { swap = Fn<DSwapInterval>("wglSwapIntervalEXT"); if (swap != null) swap(0); } catch (Exception) { }

                string ver = "", ven = "";
                try { ver = Marshal.PtrToStringAnsi(glGetString(0x1F02)); ven = Marshal.PtrToStringAnsi(glGetString(0x1F00)); } catch (Exception) { }
                GlVersion = ver ?? ""; GlVendor = ven ?? "";
                uint prog = 0; int lT = -1, lRes = -1, lIt = -1;
                DUse use = null; DUniform1f u1 = null; DUniform2f u2 = null;
                try
                {
                    DCreateShader cs0 = Fn<DCreateShader>("glCreateShader"); DShaderSource ss = Fn<DShaderSource>("glShaderSource"); DCompile co = Fn<DCompile>("glCompileShader");
                    DGetiv gsi = Fn<DGetiv>("glGetShaderiv"), gpi = Fn<DGetiv>("glGetProgramiv");
                    DCreateProgram cp = Fn<DCreateProgram>("glCreateProgram"); DAttach at = Fn<DAttach>("glAttachShader"); DLink lk = Fn<DLink>("glLinkProgram");
                    DGetUniform gu = Fn<DGetUniform>("glGetUniformLocation");
                    use = Fn<DUse>("glUseProgram"); u1 = Fn<DUniform1f>("glUniform1f"); u2 = Fn<DUniform2f>("glUniform2f");
                    if (cs0 != null && ss != null && co != null && cp != null && at != null && lk != null && gu != null && use != null && u1 != null && u2 != null && gsi != null && gpi != null)
                    {
                        uint sh = cs0(0x8B30);
                        ss(sh, 1, new string[] { FS }, IntPtr.Zero); co(sh);
                        int ok; gsi(sh, 0x8B81, out ok);
                        if (ok != 0)
                        {
                            uint pr = cp(); at(pr, sh); lk(pr);
                            gpi(pr, 0x8B82, out ok);
                            if (ok != 0) { prog = pr; lT = gu(pr, "t"); lRes = gu(pr, "res"); lIt = gu(pr, "it"); }
                        }
                    }
                }
                catch (Exception) { prog = 0; }
                int NSTR = Math.Max(10000, Math.Min(1000000, Bench.FurCount)); const float SHIT = 1200f;
                LoadTxt = NSTR.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + " pelos" + (prog != 0 ? " + shader " + ((int)SHIT) + " it" : "");

                // pelos: bola de pelos dourada (esfera)
                int N = NSTR;
                float[] pos = new float[N * 6], col = new float[N * 8];
                Random rnd = new Random(3);
                for (int i = 0; i < N; i++)
                {
                    double z = rnd.NextDouble() * 2 - 1, ph = rnd.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - z * z);
                    double nx = rr * Math.Cos(ph), ny = z, nz = rr * Math.Sin(ph);
                    double R0 = 0.5;
                    double u = rnd.NextDouble();
                    double len = 0.28 + u * u * 0.62;
                    double bx = nx * R0, by = ny * R0, bz = nz * R0;
                    double ex = nx * (R0 + len) + (rnd.NextDouble() - 0.5) * 0.1, ey = ny * (R0 + len) + (rnd.NextDouble() - 0.5) * 0.1, ez = nz * (R0 + len) + (rnd.NextDouble() - 0.5) * 0.1;
                    pos[i * 6] = (float)bx; pos[i * 6 + 1] = (float)by; pos[i * 6 + 2] = (float)bz;
                    pos[i * 6 + 3] = (float)ex; pos[i * 6 + 4] = (float)ey; pos[i * 6 + 5] = (float)ez;
                    float tone = (float)rnd.NextDouble();
                    col[i * 8] = 0.32f; col[i * 8 + 1] = 0.17f; col[i * 8 + 2] = 0.04f; col[i * 8 + 3] = 0.85f;
                    col[i * 8 + 4] = 0.85f + 0.15f * tone; col[i * 8 + 5] = 0.62f + 0.3f * tone; col[i * 8 + 6] = 0.18f + 0.4f * tone; col[i * 8 + 7] = 0.8f;
                }
                // esfera dourada pixelada (celulas em unidades de tela)
                const int SC = 26;
                float[,] pal = { { 0.42f, 0.26f, 0.04f }, { 0.62f, 0.4f, 0.06f }, { 0.8f, 0.55f, 0.08f }, { 0.93f, 0.7f, 0.14f }, { 0.98f, 0.8f, 0.25f }, { 1f, 0.9f, 0.5f }, { 1f, 0.97f, 0.78f } };
                List<float> cells = new List<float>();
                for (int cy = 0; cy < SC; cy++)
                    for (int cx = 0; cx < SC; cx++)
                    {
                        double px = (cx + 0.5) / SC * 2 - 1, py = (cy + 0.5) / SC * 2 - 1, d2 = px * px + py * py;
                        if (d2 > 1) continue;
                        double pz = Math.Sqrt(1 - d2);
                        double lt = px * -0.5 + py * 0.55 + pz * 0.65;
                        lt += (((cx * 7 + cy * 13 + cx * cy) % 5) - 2) * 0.05;
                        int lev = (int)Math.Max(0, Math.Min(6, Math.Floor((lt + 0.1) * 5.2)));
                        if (d2 > 0.86) lev = Math.Max(0, lev - 2);
                        cells.Add((float)px); cells.Add((float)py); cells.Add(pal[lev, 0]); cells.Add(pal[lev, 1]); cells.Add(pal[lev, 2]);
                    }
                hv = GCHandle.Alloc(pos, GCHandleType.Pinned); hc = GCHandle.Alloc(col, GCHandleType.Pinned);
                Renderer = rend ?? "";
                Bench.Extra = rend + "  |  bola de pelos OpenGL (" + LoadTxt + ")";
                State = 1;
                DateTime t0 = DateTime.UtcNow;
                hudBmp = new Bitmap(2048, 1024, PixelFormat.Format32bppArgb); hudPx = new byte[2048 * 1024 * 4]; hudPin = GCHandle.Alloc(hudPx, GCHandleType.Pinned);
                while (!quit && !Bench.Stop)
                {
                    float t = (float)(DateTime.UtcNow - t0).TotalSeconds;
                    int w = vw, h = vh;
                    glViewport(0, 0, w, h);
                    glClearColor(0.02f, 0.02f, 0.03f, 1f);
                    glClear(0x4000 | 0x100);
                    glDisable(0xB71); glDisable(0xBE2); glDisable(0xDE1);
                    double asp0 = w / (double)h;
                    glDepthMask(1);
                    glMatrixMode(0x1701); glLoadIdentity(); glMatrixMode(0x1700); glLoadIdentity();
                    float sx = 1f / (float)asp0;
                    if (prog != 0)
                    {
                        use(prog); u1(lT, t); u2(lRes, w, h); u1(lIt, SHIT);
                        glBegin(7); glVertex2f(-1, -1); glVertex2f(1, -1); glVertex2f(1, 1); glVertex2f(-1, 1); glEnd();
                        use(0);
                    }
                    else
                    {
                        glColor4f(0.2f, 0.2f, 0.24f, 1f);
                        glBegin(1);
                        for (int sp = 0; sp < 24; sp++)
                        {
                            double an = sp * Math.PI / 12;
                            glVertex2f(0, 0); glVertex2f((float)Math.Cos(an) * 2.2f * sx, (float)Math.Sin(an) * 2.2f);
                        }
                        for (int rg = 1; rg <= 11; rg++)
                        {
                            float rad = rg * 0.2f;
                            for (int sg = 0; sg < 72; sg++)
                            {
                                double a1 = sg * Math.PI / 36, a2 = (sg + 1) * Math.PI / 36;
                                glVertex2f((float)Math.Cos(a1) * rad * sx, (float)Math.Sin(a1) * rad); glVertex2f((float)Math.Cos(a2) * rad * sx, (float)Math.Sin(a2) * rad);
                            }
                        }
                        glEnd();
                    }
                    // esfera pixelada opaca (escreve profundidade: esconde os pelos atras dela)
                    glEnable(0xB71);
                    float cr = 0.30f, cs = cr / SC;
                    glBegin(7);
                    for (int ci = 0; ci < cells.Count; ci += 5)
                    {
                        float qx = cells[ci] * cr * sx, qy = cells[ci + 1] * cr, hx = cs * sx, hy = cs;
                        glColor4f(cells[ci + 2], cells[ci + 3], cells[ci + 4], 1f);
                        glVertex3f(qx - hx, qy - hy, 0.5f); glVertex3f(qx + hx, qy - hy, 0.5f); glVertex3f(qx + hx, qy + hy, 0.5f); glVertex3f(qx - hx, qy + hy, 0.5f);
                    }
                    glEnd();
                    glDepthMask(0);
                    double asp = w / (double)h;
                    glMatrixMode(0x1701); glLoadIdentity(); glFrustum(-asp * 0.1, asp * 0.1, -0.1, 0.1, 0.2, 50);
                    glMatrixMode(0x1700); glLoadIdentity();
                    glTranslatef(0, 0, -3.4f); glRotatef(30f + 12f * (float)Math.Sin(t * 0.4), 1, 0, 0); glRotatef(t * 35f, 0, 1, 0);
                    glEnable(0xBE2); glBlendFunc(0x302, 0x303); glLineWidth(2f);
                    glEnableClientState(0x8074); glEnableClientState(0x8076);
                    glVertexPointer(3, 0x1406, 0, hv.AddrOfPinnedObject());
                    glColorPointer(4, 0x1406, 0, hc.AddrOfPinnedObject());
                    for (int p = 0; p < 2; p++)
                    {
                        glPushMatrix(); glRotatef(p * 0.35f, 0, 1, 0);
                        glDrawArrays(1, 0, N * 2);
                        glPopMatrix();
                    }
                    glDisableClientState(0x8074); glDisableClientState(0x8076);
                    glLineWidth(1f); glDepthMask(1); glDisable(0xB71);
                    frCnt++;
                    if ((DateTime.UtcNow - frT).TotalSeconds >= 0.5) { fpsNow = frCnt / (DateTime.UtcNow - frT).TotalSeconds; frCnt = 0; frT = DateTime.UtcNow; }
                    if (hudOk)
                    {
                        try
                        {
                            if (hudTex == 0) { glGenTextures(1, out hudTex); glBindTexture(0xDE1, hudTex); glTexParameteri(0xDE1, 0x2801, 0x2601); glTexParameteri(0xDE1, 0x2800, 0x2601); }
                            glBindTexture(0xDE1, hudTex);
                            if ((DateTime.UtcNow - hudT).TotalMilliseconds >= 200 || hudW != w || hudH != h)
                            {
                                hudT = DateTime.UtcNow; hudW = w; hudH = h;
                                HudRender(w, h, rend ?? "");
                                glTexImage2D(0xDE1, 0, 4, 2048, 1024, 0, 0x1908, 0x1401, hudPin.AddrOfPinnedObject());
                            }
                            glMatrixMode(0x1701); glLoadIdentity(); glMatrixMode(0x1700); glLoadIdentity();
                            glDisable(0xB71); glEnable(0xDE1); glEnable(0xBE2); glBlendFunc(0x302, 0x303); glColor4f(1f, 1f, 1f, 1f);
                            float tu = hudPW / 2048f, tv = hudPH / 1024f;
                            glBegin(7);
                            glTexCoord2f(0, 0); glVertex2f(-1, 1); glTexCoord2f(tu, 0); glVertex2f(1, 1);
                            glTexCoord2f(tu, tv); glVertex2f(1, -1); glTexCoord2f(0, tv); glVertex2f(-1, -1);
                            glEnd();
                            glDisable(0xDE1);
                        }
                        catch (Exception) { hudOk = false; }
                    }
                    SwapBuffers(hdc);
                    Th.Interlocked.Increment(ref Bench.Counter);
                }
            }
            catch (Exception ex) { Error = ex.Message; State = 2; }
            finally
            {
                try { if (hv.IsAllocated) hv.Free(); if (hc.IsAllocated) hc.Free(); if (hudPin.IsAllocated) hudPin.Free(); if (hudBmp != null) hudBmp.Dispose(); } catch (Exception) { }
                try { wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); if (rc != IntPtr.Zero) wglDeleteContext(rc); if (hdc != IntPtr.Zero) ReleaseDC(hwnd, hdc); } catch (Exception) { }
            }
        }
    }


    static class Tpu
    {
        public static string Query(string name, bool gpu)
        {
            string n = name ?? "";
            n = Regex.Replace(n, @"\((R|TM)\)", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"@.*$", "");
            n = Regex.Replace(n, @"\bwith\b.*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\b\d+(st|nd|rd|th) Gen\b", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\b\d+-Core\b", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\b(NVIDIA|GeForce|AMD|Intel|Corporation|Graphics|Processor|CPU|Series)\b", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+", " ").Trim();
            return n;
        }
        public static void Open(string name, bool gpu, IWin32Window owner)
        {
            string q = Query(name, gpu);
            if (q.Length == 0) { Dlg.Show(owner, "Nome do hardware não identificado.", "BunoTunner"); return; }
            string url = "https://www.techpowerup.com/" + (gpu ? "gpu-specs/" : "cpu-specs/") + "?q=" + Uri.EscapeDataString(q);
            try { ProcessStartInfo psi = new ProcessStartInfo(url); psi.UseShellExecute = true; Process.Start(psi); }
            catch (Exception ex) { Dlg.Show(owner, "Não foi possível abrir o navegador: " + ex.Message, "BunoTunner"); }
        }
    }

    // ------------------------------------------------------------------
    //  Motor dos testes de estresse (GPU, CPU, FPU, RAM, SSD)
    // ------------------------------------------------------------------
    static class Bench
    {
        public static volatile bool Stop;
        public static int FurCount = 250000;
        static readonly string[] VendorWords = { "nvidia", "amd", "intel", "corporation", "graphics", "series", "generation", "laptop", "gpu", "pcie", "sse2", "microsoft" };
        static List<string> NameTokens(string s)
        {
            List<string> l = new List<string>();
            if (string.IsNullOrEmpty(s)) return l;
            int sl = s.IndexOf('/'); if (sl > 0) s = s.Substring(0, sl);
            foreach (string w in Regex.Split(s.ToLower(), "[^a-z0-9]+"))
                if (w.Length >= 2 && Array.IndexOf(VendorWords, w) < 0 && w != "r" && w != "tm") l.Add(w);
            return l;
        }
        // compara o nome da GPU (WMI/NVML) com o do OpenGL/OpenCL, tolerando diferencas de formato
        public static bool GpuNameMatch(string a, string b)
        {
            List<string> ta = NameTokens(a), tb = NameTokens(b);
            if (ta.Count == 0 || tb.Count == 0) return false;
            List<string> small = ta.Count <= tb.Count ? ta : tb, big = ta.Count <= tb.Count ? tb : ta;
            int hit = 0;
            foreach (string w in small) if (big.Contains(w)) hit++;
            return hit == small.Count || (hit >= 2 && hit * 10 >= small.Count * 8);
        }
        public static long Counter, Errors;
        public static volatile string Extra = "", Fail;
        // estado REAL dos testes de RAM e SSD (o desenho do render le estes campos)
        public const int RamChunk = 8 * 1024 * 1024;                    // palavras de 8 bytes por bloco (64 MB)
        public static volatile byte[] RamState = new byte[0];            // por bloco: 0 parado, 1 gravando, 2 conferindo, 3 conferido sem erro, 4 com erro
        public static volatile int[] RamProg = new int[0];               // 0..1000 dentro da fase atual do bloco
        public static volatile int RamRound, RamViewPos; public static long RamPattern;
        public static volatile long[] RamView;                           // bloco que a primeira thread esta gravando/conferindo agora
        public static volatile byte[] SsdMap = new byte[128];            // mapa do arquivo de teste: 0 intocado, 1 gravado, 2 lido e conferido, 3 erro
        public static int[] SsdHit = new int[512];                       // ultimo acesso 4K aleatorio por regiao do arquivo (TickCount)
        public static volatile int SsdPhase, SsdMb, SsdPos;              // fase: 0 escrita seq, 1 leitura seq, 2 leitura 4K aleatoria
        static int alive;
        public static bool Alive { get { return Th.Interlocked.CompareExchange(ref alive, 0, 0) > 0; } }

        [StructLayout(LayoutKind.Sequential)]
        struct MemStatus
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStatus m);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(IntPtr h, IntPtr buf, uint n, out uint read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(IntPtr h, IntPtr buf, uint n, out uint written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetFilePointerEx(IntPtr h, long dist, out long newPos, uint method);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        public static long AvailPhys()
        {
            MemStatus m = new MemStatus();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MemStatus));
            return GlobalMemoryStatusEx(ref m) ? (long)m.ullAvailPhys : 0;
        }

        static void Spawn(Th.ParameterizedThreadStart fn, object arg, string name)
        {
            Th.Interlocked.Increment(ref alive);
            Th.Thread t = new Th.Thread(delegate(object o)
            {
                try { fn(o); }
                catch (Exception ex) { Fail = name + ": " + ex.Message; }
                finally { Th.Interlocked.Decrement(ref alive); }
            });
            t.IsBackground = true; t.Priority = Th.ThreadPriority.BelowNormal; t.Name = name;
            t.Start(arg);
        }

        // kind: 0 GPU, 1 CPU, 2 FPU, 3 RAM, 4 SSD
        public static void Start(int kind, int threads, int ramPct, string drive, int ssdMb, string gpuName)
        {
            Stop = false; Counter = 0; Errors = 0; Extra = ""; Fail = null;
            switch (kind)
            {
                case 0:
                    Spawn(delegate(object o) { GpuRun((string)o); }, gpuName, "bench-gpu");
                    break;
                case 1:
                    for (int i = 0; i < threads; i++) Spawn(CpuWorker, i, "bench-cpu");
                    break;
                case 2:
                    for (int i = 0; i < threads; i++) Spawn(FpuWorker, i, "bench-fpu");
                    break;
                case 3:
                    {
                        long avail = AvailPhys();
                        long total = Math.Min(avail * ramPct / 100, avail - 1024L * 1048576L);
                        if (total < 128L * 1048576L) { Fail = "Memória livre insuficiente para o teste."; break; }
                        int th = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
                        long per = total / th;
                        int nPer = (int)Math.Max(1, per / (RamChunk * 8L));
                        RamState = new byte[th * nPer]; RamProg = new int[th * nPer]; RamRound = 0; RamView = null; RamViewPos = 0;
                        for (int i = 0; i < th; i++) { int bi = i * nPer; Spawn(delegate(object o) { RamWorker(per, bi, nPer); }, i, "bench-ram"); }
                        Extra = "Usando " + (total >> 20) + " MB em " + th + " thread(s)";
                        break;
                    }
                case 4:
                    Spawn(delegate(object o) { SsdRun(drive, ssdMb); }, null, "bench-ssd");
                    break;
            }
        }

        // ---------- CPU (inteiros) ----------
        [MethodImpl(MethodImplOptions.NoInlining)]
        static ulong IntKernel(ulong x, int n)
        {
            ulong acc = 0;
            for (int i = 0; i < n; i++)
            {
                x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                acc += x * 0x9E3779B97F4A7C15UL;
                acc = (acc << 5) | (acc >> 59);
            }
            return acc ^ x;
        }
        static void CpuWorker(object o)
        {
            const int N = 1500000;
            ulong seed = 0x123456789ABCDEFUL;
            ulong refv = IntKernel(seed, N);
            while (!Stop)
            {
                if (IntKernel(seed, N) != refv) Th.Interlocked.Increment(ref Errors);
                Th.Interlocked.Add(ref Counter, (long)N * 10);
            }
        }

        // ---------- FPU (matrizes de double) ----------
        [MethodImpl(MethodImplOptions.NoInlining)]
        static double Mat(double[] a, double[] b, double[] c, int n)
        {
            Array.Clear(c, 0, c.Length);
            for (int i = 0; i < n; i++)
                for (int k = 0; k < n; k++)
                {
                    double aik = a[i * n + k];
                    int bi = k * n, ci = i * n;
                    for (int j = 0; j < n; j++) c[ci + j] += aik * b[bi + j];
                }
            double s = 0;
            for (int i = 0; i < c.Length; i++) s += c[i];
            return s;
        }
        static void FpuWorker(object o)
        {
            const int M = 96;
            double[] a = new double[M * M], b = new double[M * M], c = new double[M * M];
            for (int i = 0; i < a.Length; i++) { a[i] = ((i * 37) % 101) / 101.0 + 0.5; b[i] = ((i * 53) % 89) / 89.0 + 0.25; }
            double refv = Mat(a, b, c, M);
            while (!Stop)
            {
                if (Mat(a, b, c, M) != refv) Th.Interlocked.Increment(ref Errors);
                Th.Interlocked.Add(ref Counter, 2L * M * M * M);
            }
        }

        // ---------- RAM ----------
        static void RamWorker(long bytes, int baseIdx, int n)
        {
            int chunk = RamChunk;
            List<long[]> bufs = new List<long[]>();
            for (int i = 0; i < n && !Stop; i++) bufs.Add(new long[chunk]);
            ulong[] pats = { 0xAAAAAAAAAAAAAAAAUL, 0x5555555555555555UL, 0xFFFFFFFF00000000UL, 0x00000000FFFFFFFFUL };
            int round = 0; const int SL = 1 << 16;
            while (!Stop)
            {
                long p = (long)pats[round++ & 3];
                if (baseIdx == 0) { RamPattern = p; RamRound = round; }
                for (int bi = 0; bi < bufs.Count; bi++)
                {
                    long[] b = bufs[bi]; int idx = baseIdx + bi;
                    if (Stop) break;
                    RamState[idx] = 1; if (baseIdx == 0) RamView = b;
                    for (int s0 = 0; s0 < b.Length && !Stop; s0 += SL)
                    {
                        int e0 = Math.Min(b.Length, s0 + SL);
                        for (int i = s0; i < e0; i++) b[i] = p ^ i;
                        RamProg[idx] = (int)(e0 * 1000L / b.Length); if (baseIdx == 0) RamViewPos = s0;
                    }
                    Th.Interlocked.Add(ref Counter, (long)b.Length * 8);
                    RamState[idx] = 2; RamProg[idx] = 0;
                    long bad = 0;
                    for (int s0 = 0; s0 < b.Length && !Stop; s0 += SL)
                    {
                        int e0 = Math.Min(b.Length, s0 + SL);
                        for (int i = s0; i < e0; i++) if (b[i] != (p ^ i)) bad++;
                        RamProg[idx] = (int)(e0 * 1000L / b.Length); if (baseIdx == 0) RamViewPos = s0;
                    }
                    if (bad > 0) Th.Interlocked.Add(ref Errors, bad);
                    RamState[idx] = (byte)(bad > 0 ? 4 : 3);
                    Th.Interlocked.Add(ref Counter, (long)b.Length * 8);
                }
            }
        }

        // ---------- SSD (E/S sem cache do Windows, com verificacao) ----------
        static void SsdRun(string drive, int mb)
        {
            string path = Path.Combine(drive, "BunoTunner_bench.tmp");
            try
            {
                DriveInfo di = new DriveInfo(drive.Substring(0, 1));
                if (di.AvailableFreeSpace < (long)mb * 1048576L + 512L * 1048576L) { Fail = "Pouco espaço livre no drive " + drive; return; }
            }
            catch (Exception) { }
            const uint RW = 0xC0000000u;
            const uint FLAGS = 0x20000000u | 0x80000000u | 0x04000000u;   // NO_BUFFERING | WRITE_THROUGH | DELETE_ON_CLOSE
            IntPtr h = CreateFile(path, RW, 0, IntPtr.Zero, 2, FLAGS, IntPtr.Zero);
            if (h == new IntPtr(-1)) { Fail = "Não consegui criar o arquivo de teste em " + drive + " (erro " + Marshal.GetLastWin32Error() + ")"; return; }
            const int BLK = 1 << 20;
            IntPtr raw = Marshal.AllocHGlobal(BLK + 4096);
            try
            {
                IntPtr buf = new IntPtr((raw.ToInt64() + 4095) & ~4095L);
                byte[] rnd = new byte[BLK];
                new Random(12345).NextBytes(rnd);
                Marshal.Copy(rnd, 0, buf, BLK);
                Random rand = new Random();
                double w = 0, rd = 0, iops = 0;
                long np; uint done;
                SsdMap = new byte[128]; Array.Clear(SsdHit, 0, SsdHit.Length); SsdMb = mb; SsdPos = 0; SsdPhase = 0;
                while (!Stop)
                {
                    Array.Clear(SsdMap, 0, SsdMap.Length); SsdPhase = 0; SsdPos = 0;
                    SetFilePointerEx(h, 0, out np, 0);
                    Stopwatch sw = Stopwatch.StartNew();
                    int wrote = 0;
                    for (int i = 0; i < mb && !Stop; i++)
                    {
                        Marshal.WriteInt32(buf, 0, i); Marshal.WriteInt32(buf, 4, ~i);
                        if (!WriteFile(h, buf, BLK, out done, IntPtr.Zero)) { Fail = "Erro de escrita (" + Marshal.GetLastWin32Error() + ")"; return; }
                        Th.Interlocked.Add(ref Counter, done); wrote++;
                        SsdPos = i; SsdMap[Math.Min(127, (int)((long)i * 128 / mb))] = 1;
                    }
                    w = wrote / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    Extra = "Escrita seq " + w.ToString("0") + " MB/s";
                    if (Stop) break;

                    SetFilePointerEx(h, 0, out np, 0);
                    sw.Restart(); SsdPhase = 1; SsdPos = 0;
                    for (int i = 0; i < mb && !Stop; i++)
                    {
                        if (!ReadFile(h, buf, BLK, out done, IntPtr.Zero)) { Fail = "Erro de leitura (" + Marshal.GetLastWin32Error() + ")"; return; }
                        bool badBlk = Marshal.ReadInt32(buf, 0) != i || Marshal.ReadInt32(buf, 4) != ~i;
                        if (badBlk) Th.Interlocked.Increment(ref Errors);
                        Th.Interlocked.Add(ref Counter, done);
                        SsdPos = i; int mc = Math.Min(127, (int)((long)i * 128 / mb)); if (badBlk) SsdMap[mc] = 3; else if (SsdMap[mc] != 3) SsdMap[mc] = 2;
                    }
                    rd = mb / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    Extra = "Escrita seq " + w.ToString("0") + " MB/s | Leitura seq " + rd.ToString("0") + " MB/s";
                    if (Stop) break;

                    sw.Restart(); SsdPhase = 2;
                    int ops = 0;
                    while (sw.ElapsedMilliseconds < 3000 && !Stop)
                    {
                        int uidx = rand.Next(0, mb * 256);
                        long off = (long)uidx * 4096L;
                        SetFilePointerEx(h, off, out np, 0);
                        if (ReadFile(h, buf, 4096, out done, IntPtr.Zero)) { ops++; Th.Interlocked.Add(ref Counter, done); SsdHit[(int)(uidx * 512L / (mb * 256L))] = Environment.TickCount | 1; }
                    }
                    iops = ops / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    Extra = "Escrita seq " + w.ToString("0") + " MB/s | Leitura seq " + rd.ToString("0") + " MB/s | 4K aleatório " + iops.ToString("0") + " IOPS";
                }
            }
            finally { Marshal.FreeHGlobal(raw); CloseHandle(h); }
        }

        // ---------- GPU (OpenCL) ----------
        const string ClSrc =
            "__kernel void burn(__global float* o, const int iters)\n" +
            "{\n" +
            "  int g = get_global_id(0);\n" +
            "  float a = 1.0f + g * 1e-7f, b = 0.9999f, c = 1.0001f, d = 0.5f, e = 0.25f, f = 0.125f, h = 0.75f, k = 1.5f;\n" +
            "  for (int i = 0; i < iters; i++)\n" +
            "  {\n" +
            "    a = mad(a, b, c); d = mad(d, b, c); e = mad(e, b, c); f = mad(f, b, c);\n" +
            "    h = mad(h, b, c); k = mad(k, b, c); b = mad(b, 0.99999f, 1e-5f); c = mad(c, 0.99999f, 1e-5f);\n" +
            "  }\n" +
            "  o[g] = a + d + e + f + h + k + b + c;\n" +
            "}\n" +
            "__kernel void membw(__global const float* a, __global float* b, const int n)\n" +
            "{\n" +
            "  int g = get_global_id(0);\n" +
            "  int stride = get_global_size(0);\n" +
            "  for (int i = g; i < n; i += stride) b[i] = a[i] * 2.0f + 1.0f;\n" +
            "}\n";

        static void GpuRun(string gpuName)
        {
            uint np;
            if (Cl.clGetPlatformIDs(0, null, out np) != 0 || np == 0) { Fail = "OpenCL não encontrado (instale o driver da placa de video)."; return; }
            IntPtr[] plats = new IntPtr[np];
            Cl.clGetPlatformIDs(np, plats, out np);
            IntPtr dev = IntPtr.Zero, first = IntPtr.Zero; string devName = null, firstName = null;
            foreach (IntPtr pl in plats)
            {
                uint nd;
                if (Cl.clGetDeviceIDs(pl, 4, 0, null, out nd) != 0 || nd == 0) continue;
                IntPtr[] ds = new IntPtr[nd];
                Cl.clGetDeviceIDs(pl, 4, nd, ds, out nd);
                foreach (IntPtr d in ds)
                {
                    StringBuilder sb = new StringBuilder(256); IntPtr rs;
                    Cl.GetDevStr(d, 0x102B, (IntPtr)256, sb, out rs);
                    string n = sb.ToString().Trim();
                    if (first == IntPtr.Zero) { first = d; firstName = n; }
                    if (dev == IntPtr.Zero && gpuName != null && n.Length > 0 &&
                        (gpuName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf(gpuName, StringComparison.OrdinalIgnoreCase) >= 0 || GpuNameMatch(n, gpuName))) { dev = d; devName = n; }
                }
            }
            if (dev == IntPtr.Zero) { dev = first; devName = firstName; }
            if (dev == IntPtr.Zero) { Fail = "Nenhuma GPU encontrada pelo OpenCL."; return; }

            ulong gmem = 0, maxAlloc = 0; IntPtr rr;
            Cl.GetDevU64(dev, 0x101F, (IntPtr)8, out gmem, out rr);
            Cl.GetDevU64(dev, 0x1010, (IntPtr)8, out maxAlloc, out rr);

            int err;
            IntPtr ctx = Cl.clCreateContext(IntPtr.Zero, 1, new IntPtr[] { dev }, IntPtr.Zero, IntPtr.Zero, out err);
            if (err != 0 || ctx == IntPtr.Zero) { Fail = "OpenCL: falha ao criar contexto (" + err + ")"; return; }
            IntPtr q = IntPtr.Zero, prog = IntPtr.Zero, kBurn = IntPtr.Zero, kMem = IntPtr.Zero, bufO = IntPtr.Zero, bufA = IntPtr.Zero, bufB = IntPtr.Zero;
            try
            {
                q = Cl.clCreateCommandQueue(ctx, dev, 0, out err);
                if (err != 0) { Fail = "OpenCL: falha na fila de comandos (" + err + ")"; return; }
                prog = Cl.clCreateProgramWithSource(ctx, 1, new string[] { ClSrc }, null, out err);
                if (err != 0) { Fail = "OpenCL: falha ao carregar o programa (" + err + ")"; return; }
                err = Cl.clBuildProgram(prog, 1, new IntPtr[] { dev }, "", IntPtr.Zero, IntPtr.Zero);
                if (err != 0)
                {
                    StringBuilder log = new StringBuilder(4096); IntPtr ls;
                    Cl.clGetProgramBuildInfo(prog, dev, 0x1183, (IntPtr)4096, log, out ls);
                    Fail = "OpenCL: erro ao compilar (" + err + ") " + log.ToString().Trim();
                    return;
                }
                kBurn = Cl.clCreateKernel(prog, "burn", out err);
                kMem = Cl.clCreateKernel(prog, "membw", out err);
                const int items = 1 << 20;
                bufO = Cl.clCreateBuffer(ctx, 1, (IntPtr)(items * 4L), IntPtr.Zero, out err);
                long nel = 32L * 1024 * 1024;
                long cap = (long)Math.Min(maxAlloc, gmem / 6) / 4;
                if (cap > 1024 * 1024 && cap < nel) nel = cap;
                bufA = Cl.clCreateBuffer(ctx, 1, (IntPtr)(nel * 4), IntPtr.Zero, out err);
                bufB = Cl.clCreateBuffer(ctx, 1, (IntPtr)(nel * 4), IntPtr.Zero, out err);
                if (bufO == IntPtr.Zero || bufA == IntPtr.Zero || bufB == IntPtr.Zero) { Fail = "OpenCL: sem memória de video para o teste."; return; }

                Cl.ArgMem(kBurn, 0, (IntPtr)IntPtr.Size, ref bufO);
                Cl.ArgMem(kMem, 0, (IntPtr)IntPtr.Size, ref bufA);
                Cl.ArgMem(kMem, 1, (IntPtr)IntPtr.Size, ref bufB);
                int nint = (int)nel;
                Cl.ArgInt(kMem, 2, (IntPtr)4, ref nint);

                IntPtr[] gsz = new IntPtr[] { (IntPtr)items };
                int iters = 128, round = 0;
                Stopwatch sw = new Stopwatch();
                Extra = devName;
                while (!Stop)
                {
                    Cl.ArgInt(kBurn, 1, (IntPtr)4, ref iters);
                    sw.Restart();
                    err = Cl.clEnqueueNDRangeKernel(q, kBurn, 1, null, gsz, null, 0, IntPtr.Zero, IntPtr.Zero);
                    if (err == 0) err = Cl.clFinish(q);
                    sw.Stop();
                    if (err != 0) { Fail = "OpenCL: erro " + err + " durante o cálculo (o driver pode ter reiniciado)."; break; }
                    double ms = sw.Elapsed.TotalMilliseconds;
                    Th.Interlocked.Add(ref Counter, (long)items * iters * 16);
                    // mantem cada disparo curto (~40-150 ms) para nao acionar o TDR do Windows
                    if (ms < 40 && iters < 65536) iters *= 2; else if (ms > 150 && iters > 16) iters /= 2;
                    if ((++round & 3) == 0)
                    {
                        sw.Restart();
                        for (int i = 0; i < 6 && err == 0; i++)
                        {
                            err = Cl.clEnqueueNDRangeKernel(q, kMem, 1, null, gsz, null, 0, IntPtr.Zero, IntPtr.Zero);
                        }
                        if (err == 0) err = Cl.clFinish(q);
                        sw.Stop();
                        if (err == 0)
                        {
                            double gbs = 6.0 * nel * 8 / 1e9 / Math.Max(0.0001, sw.Elapsed.TotalSeconds);
                            Extra = devName + "  |  VRAM " + gbs.ToString("0") + " GB/s";
                        }
                    }
                }
            }
            finally
            {
                if (bufO != IntPtr.Zero) Cl.clReleaseMemObject(bufO);
                if (bufA != IntPtr.Zero) Cl.clReleaseMemObject(bufA);
                if (bufB != IntPtr.Zero) Cl.clReleaseMemObject(bufB);
                if (kBurn != IntPtr.Zero) Cl.clReleaseKernel(kBurn);
                if (kMem != IntPtr.Zero) Cl.clReleaseKernel(kMem);
                if (prog != IntPtr.Zero) Cl.clReleaseProgram(prog);
                if (q != IntPtr.Zero) Cl.clReleaseCommandQueue(q);
                Cl.clReleaseContext(ctx);
            }
        }
    }

    // ------------------------------------------------------------------
    //  Ventoinhas de todo o PC (GPU + CPU_FAN + bombas + gabinete + coolers)
    // ------------------------------------------------------------------
    class FanCh
    {
        public string Name, Origin, Key, Custom = "";
        public Gpu Gpu; public Sn Rpm; public LHM.ISensor Ctl;
        public int Mode, Manual = 50, CMinT = 40, CMinP = 30, CMaxT = 80, CMaxP = 100, Last = -1, RpmMax;
        public bool Boost;
        public bool Controllable { get { return (Gpu != null && Gpu.CanFan) || Ctl != null; } }
        public string Display { get { return Custom.Length > 0 ? Custom : Name; } }

        // Tipo da ventoinha deduzido pelo nome (voce pode renomear: inclua CPU, Bomba ou Gabinete)
        public string RoleName
        {
            get
            {
                if (Gpu != null) return "GPU";
                string n = (Custom + " " + Name).ToLower();
                if (n.Contains("pump") || n.Contains("aio") || n.Contains("water") || n.Contains("liquid") || n.Contains("bomba")) return "Bomba/AIO";
                if (n.Contains("cpu") || n.Contains("proc")) return "CPU";
                if (n.Contains("chassis") || n.Contains("system") || n.Contains("sys") || n.Contains("case") || n.Contains("front") || n.Contains("rear") ||
                    n.Contains("top") || n.Contains("cha") || n.Contains("gabinete") || n.Contains("frontal") || n.Contains("traseira")) return "Gabinete";
                return "Outra";
            }
        }
        // minimo de seguranca: bomba de AIO nunca abaixo de 60%
        public int MinPct { get { string r = RoleName; return r == "GPU" ? 0 : (r == "Bomba/AIO" ? 60 : 20); } }

        public int? Pct()
        {
            if (Gpu != null) return Gpu.FanPct();
            if (Ctl != null) { float? v = Ctl.Value; return v.HasValue ? (int?)Math.Round(v.Value) : null; }
            return null;
        }
        public int? RpmNow()
        {
            if (Gpu != null) return Gpu.FanRpm();
            return (Rpm != null && Rpm.Last.HasValue) ? (int?)Math.Round(Rpm.Last.Value) : null;
        }
        public bool Set(int pct, out string msg)
        {
            pct = Math.Max(MinPct, Math.Min(100, pct));
            if (Gpu != null) return Gpu.SetFan(pct, out msg);
            if (Ctl != null && Ctl.Control != null)
            {
                try { Ctl.Control.SetSoftware(pct); msg = "OK: " + pct + "%"; return true; }
                catch (Exception ex) { msg = ex.Message; return false; }
            }
            msg = "Esta ventoinha e somente leitura."; return false;
        }
        public bool Auto(out string msg)
        {
            if (Gpu != null) return Gpu.SetFanAuto(out msg);
            if (Ctl != null && Ctl.Control != null)
            {
                try { Ctl.Control.SetDefault(); msg = "OK: automático"; return true; }
                catch (Exception ex) { msg = ex.Message; return false; }
            }
            msg = "Esta ventoinha e somente leitura."; return false;
        }
    }

    static class FanHub
    {
        public static List<FanCh> All = new List<FanCh>();
        static bool built;
        static string Digits(string t)
        {
            int e = t.Length - 1;
            while (e >= 0 && !char.IsDigit(t[e])) e--;
            if (e < 0) return "";
            int b = e;
            while (b > 0 && char.IsDigit(t[b - 1])) b--;
            return t.Substring(b, e - b + 1);
        }
        public static void Build()
        {
            if (built) return;
            built = true;
            foreach (Gpu g in SensorHub.Gpus)
            {
                FanCh c = new FanCh(); c.Gpu = g; c.Name = "Ventoinhas da GPU"; c.Origin = g.Name; c.Key = "gpu:" + g.Name;
                All.Add(c);
            }
            List<Sn> used = new List<Sn>();
            foreach (Sn s in SensorHub.PcSensors)
            {
                if (s.Type != "Control" || s.Ls == null || s.Ls.Control == null) continue;
                FanCh c = new FanCh(); c.Ctl = s.Ls; c.Name = s.Name; c.Origin = s.Group; c.Key = s.Key;
                string d = Digits(s.Name);
                foreach (Sn f in SensorHub.PcSensors)
                    if (f.Type == "Fan" && f.Group == s.Group && !used.Contains(f) && (f.Name == s.Name || (d.Length > 0 && Digits(f.Name) == d)))
                    { c.Rpm = f; used.Add(f); break; }
                All.Add(c);
            }
            foreach (Sn f in SensorHub.PcSensors)
            {
                if (f.Type != "Fan" || used.Contains(f)) continue;
                FanCh c = new FanCh(); c.Rpm = f; c.Name = f.Name; c.Origin = f.Group; c.Key = f.Key;
                All.Add(c);
            }
            foreach (FanCh c in All) c.Custom = Settings.Get("fan.name." + c.Key, "");
        }
    }

    class FanPage : Panel
    {
        Gpu cur;
        ListView lv = new XpList();
        XpOption optAuto, optManual, chkSafe, chkRestore;
        XpSlider sl; XpText txPct, txRpm, txSafe;
        Label lblMsg, lblSel, lblHint; XpGroup gList, gMode, gApply;
        Dictionary<FanCh, ListViewItem> items = new Dictionary<FanCh, ListViewItem>();
        List<FanCh> identList = new List<FanCh>();
        DateTime identUntil = DateTime.MinValue;
        LTip tip = new LTip();

        public FanPage()
        {
            Size = new Size(676, 586); Dock = DockStyle.Fill;   // tamanho inicial = o da area da pagina, para os ancoramentos (Anchor) funcionarem
            FanHub.Build();
            tip.InitialDelay = 300; tip.AutoPopDelay = 9000;
            BuildList(); BuildMode(); BuildApply(); BuildMenu();
            LoadUi();
            lv.SelectedIndexChanged += delegate { UpdateSel(); };
            UpdateSel();
        }

        // lembra a ultima configuracao da tela (modo, %, seguranca) entre as aberturas do programa
        void LoadUi()
        {
            int v;
            if (int.TryParse(Settings.Get("fan.ui.pct", "50"), out v)) sl.Value = Math.Max(0, Math.Min(100, v));
            chkSafe.Checked = Settings.Get("fan.ui.safe", "1") == "1";
            chkRestore.Checked = Settings.Get("fan.ui.restore", "1") == "1";
            if (int.TryParse(Settings.Get("fan.ui.safet", "85"), out v)) txSafe.Text = Math.Max(60, Math.Min(100, v)).ToString();
            if (Settings.Get("fan.ui.mode", "0") == "1") optManual.Checked = true; else optAuto.Checked = true;
            sl.ValueChanged += delegate { Settings.Set("fan.ui.pct", sl.Value.ToString()); };
            chkSafe.CheckedChanged += delegate { Settings.Set("fan.ui.safe", chkSafe.Checked ? "1" : "0"); };
            chkRestore.CheckedChanged += delegate { Settings.Set("fan.ui.restore", chkRestore.Checked ? "1" : "0"); };
            optManual.CheckedChanged += delegate { Settings.Set("fan.ui.mode", optManual.Checked ? "1" : "0"); };
            txSafe.Box.Leave += delegate { int t = Math.Max(60, Math.Min(100, UI.Int(txSafe, 85))); txSafe.Text = t.ToString(); Settings.Set("fan.ui.safet", t.ToString()); };
        }

        void UpdateSel()
        {
            int n = 0, tot = 0;
            foreach (FanCh c in FanHub.All) if (c.Controllable) tot++;
            foreach (ListViewItem it in lv.SelectedItems) { FanCh c = it.Tag as FanCh; if (c != null && c.Controllable) n++; }
            lblSel.Text = n + Lang.Pick(" de ", " of ") + tot + Lang.Pick(" controláveis selecionadas", " controllable selected");
            lblSel.ForeColor = n > 0 ? Xp.GroupText : Xp.Dim;
        }

        public void SetGpu(Gpu g) { cur = g; }

        void BuildList()
        {
            XpGroup g = new XpGroup(); gList = g; g.Title = "Ventoinhas detectadas (Ctrl/Shift = várias)"; g.SetBounds(0, 0, 676, 286); g.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            ((XpList)lv).AutoFit = true; ((XpList)lv).SelfLayout = true; ((XpList)lv).Fitted += delegate { Relayout(); }; lv.SetBounds(10, 54, 656, 188); lv.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; lv.View = View.Details; lv.FullRowSelect = true; lv.GridLines = true;
            lv.MultiSelect = true; lv.HideSelection = false; lv.BackColor = Xp.EditBg; lv.Font = Xp.Ui;
            lv.Columns.Add("Ventoinha", 180); lv.Columns.Add("Tipo", 90); lv.Columns.Add("Origem", 170); lv.Columns.Add("RPM", 60);
            lv.Columns.Add("%", 50); lv.Columns.Add("Modo", 102);
            UI.DoubleBuffer(lv);
            g.Controls.Add(lv);
            foreach (FanCh c in FanHub.All)
            {
                ListViewItem it = new ListViewItem(new string[] { c.Display, c.Controllable ? c.RoleName : c.RoleName + " (só leitura)", c.Origin, "-", "-", "-" });
                it.Tag = c;
                if (!c.Controllable) it.ForeColor = Xp.Dim;
                lv.Items.Add(it); items[c] = it;
            }
            if (FanHub.All.Count == 0)
                lv.Items.Add(new ListViewItem(new string[] { "Nenhuma ventoinha encontrada", "", "A placa-mae pode não expor as ventoinhas ao Windows", "", "", "" }));
            lblSel = UI.Lbl(g, "", 10, 27, Xp.UiBold, Xp.Dim); lblSel.AutoSize = false; lblSel.SetBounds(10, 27, 400, 18); lblSel.Visible = false;
            lv.DoubleClick += delegate { Rename(); };
            lv.KeyDown += delegate(object o, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem x in lv.Items) x.Selected = true; e.Handled = true; } else if (e.KeyCode == Keys.F2) { Rename(); e.Handled = true; } };
            XpButton bId = UI.Btn(g, "Identificar", 420, 22, 100, delegate { Identify(); }); bId.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            XpButton bAuto = UI.Btn(g, "Tudo automático", 526, 22, 140, delegate { AllAuto(); }); bAuto.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Label h = UI.Lbl(g, "Identificar = liga as selecionadas a 100% por 5 s para você descobrir qual é qual.  Botão direito na lista: selecionar por tipo (CPU, bombas, gabinete) ou renomear (inclua CPU, Bomba ou Gabinete no nome).", 10, 248, Xp.Ui, Xp.Dim);
            lblHint = h; h.Visible = false; h.AutoSize = false; h.SetBounds(10, 248, 656, 32);
            tip.SetToolTip(lv, "Identificar = liga as selecionadas a 100% por 5 s para você descobrir qual é qual. Botão direito: selecionar por tipo ou renomear. RPM = giros por minuto. % = potência enviada. \"só leitura\" = a placa-mae não permite controle por software. Atalhos: Ctrl+A seleciona tudo; F2 ou duplo clique renomeia.");
            Controls.Add(g);
        }

        void BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip(); m.RenderMode = ToolStripRenderMode.System;
            m.Items.Add("Selecionar todas", null, delegate { foreach (ListViewItem x in lv.Items) x.Selected = true; });
            m.Items.Add("Selecionar só as controláveis", null, delegate
            {
                foreach (ListViewItem x in lv.Items) { FanCh f = x.Tag as FanCh; x.Selected = f != null && f.Controllable; }
            });
            m.Items.Add("Inverter seleção", null, delegate { foreach (ListViewItem x in lv.Items) x.Selected = !x.Selected; });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Selecionar: CPU", null, delegate { SelectRole("CPU"); });
            m.Items.Add("Selecionar: bombas / AIO", null, delegate { SelectRole("Bomba/AIO"); });
            m.Items.Add("Selecionar: gabinete", null, delegate { SelectRole("Gabinete"); });
            m.Items.Add("Selecionar: GPU", null, delegate { SelectRole("GPU"); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Renomear...", null, delegate { Rename(); });
            m.Items.Add("Identificar (100% por 5 s)", null, delegate { Identify(); });
            Lang.Menu(m); lv.ContextMenuStrip = m;
        }

        void BuildMode()
        {
            XpGroup g = new XpGroup(); g.Title = "Modo de funcionamento das selecionadas"; g.SetBounds(0, 292, 676, 166); g.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; gMode = g;
            optAuto = UI.Opt(g, "Automático - a placa/BIOS decide (recomendado no dia a dia)", 14, 22, 520, true);
            optManual = UI.Opt(g, "Manual - velocidade fixa em %:", 14, 44, 300, true);
            sl = new XpSlider(); sl.SetBounds(34, 60, 280, 32); sl.Minimum = 0; sl.Maximum = 100; sl.Value = 50; g.Controls.Add(sl);
            txPct = UI.Txt(g, 322, 66, 44, "50"); UI.Lbl(g, "%", 370, 70);
            UI.Bind(sl, txPct);
            int px = 400;
            foreach (int pv in new int[] { 0, 25, 50, 75, 100 })
            {
                int vv = pv;
                UI.Btn(g, pv + "%", px, 62, 50, delegate { sl.Value = vv; optManual.Checked = true; }).Height = 24; px += 54;
            }
            txPct.Box.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { optManual.Checked = true; ApplySel(); } };
            UI.Lbl(g, "RPM alvo:", 34, 110);
            txRpm = UI.Txt(g, 96, 106, 60, "1200");
            txRpm.Box.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) ByRpm(); };
            XpButton bRpm = UI.Btn(g, "Definir por RPM", 166, 104, 112, delegate { ByRpm(); });
            tip.SetToolTip(bRpm, "Use \"Identificar\" antes: ele mede o RPM máximo de cada ventoinha. Enter no campo de RPM também aplica.");
            tip.SetToolTip(txRpm, "RPM desejado. Enter também aplica.");
            tip.SetToolTip(txPct, "Velocidade fixa em %. Enter também aplica.");
            tip.SetToolTip(optAuto, "A própria placa-mae ou o driver controla a velocidade pela temperatura.");
            tip.SetToolTip(optManual, "A ventoinha fica sempre na mesma velocidade, não importa a temperatura.");
            Controls.Add(g);
        }

        void BuildApply()
        {
            XpGroup g = new XpGroup(); g.Title = "Aplicar"; g.SetBounds(0, 464, 676, 80); g.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; gApply = g;
            UI.Btn(g, "Aplicar nas selecionadas", 10, 20, 170, delegate { ApplySel(); }).Height = 26;
            chkSafe = UI.Opt(g, "Segurança: forcar 100% se a temperatura passar de", 10, 54, 320, false); chkSafe.Checked = true;
            txSafe = UI.Txt(g, 336, 51, 40, "85"); UI.Lbl(g, "\u00B0C", 380, 55);
            chkRestore = UI.Opt(g, "Voltar tudo ao automático ao fechar", 420, 54, 250, false); chkRestore.Checked = true;
            tip.SetToolTip(chkSafe, "Se esquentar demais, as ventoinhas vão a 100% mesmo no modo manual.");
            Controls.Add(g);
            lblMsg = UI.Lbl(this, "", 0, 474, Xp.UiBold, Xp.Good);
            lblMsg.AutoSize = false; lblMsg.SetBounds(0, 548, 676, 38); lblMsg.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Relayout();
        }

        // empilha os blocos logo abaixo da lista (sem espaco vazio): lista -> dica -> modo -> aplicar -> mensagem
        void Relayout()
        {
            if (gList == null || gMode == null || gApply == null || lblHint == null) return;
            SuspendLayout();
            gList.Height = lv.Bottom + 10;
            gMode.Top = gList.Bottom + 6;
            gApply.Top = gMode.Bottom + 6;
            if (lblMsg != null) lblMsg.Top = gApply.Bottom + 4;
            ResumeLayout(true);
        }

        void Msg(string t, bool err) { lblMsg.ForeColor = err ? Xp.Bad : Xp.Good; lblMsg.Text = t; }

        List<FanCh> Selected()
        {
            List<FanCh> l = new List<FanCh>();
            foreach (ListViewItem it in lv.SelectedItems) { FanCh c = it.Tag as FanCh; if (c != null && c.Controllable) l.Add(c); }
            return l;
        }

        void SelectRole(string role)
        {
            lv.Focus();
            int n = 0;
            foreach (ListViewItem it in lv.Items)
            {
                FanCh c = it.Tag as FanCh;
                bool on = c != null && c.Controllable && c.RoleName == role;
                it.Selected = on; if (on) n++;
            }
            Msg(n > 0 ? n + " ventoinha(s) do tipo \"" + role + "\" selecionada(s)." : "Nenhuma ventoinha controlável do tipo \"" + role + "\" (renomeie as suas para classificar).", n == 0);
        }

        void Rename()
        {
            if (lv.SelectedItems.Count == 0) { Msg("Selecione uma ventoinha para renomear.", true); return; }
            ListViewItem it = lv.SelectedItems[0]; FanCh c = it.Tag as FanCh;
            if (c == null) return;
            string r = PromptForm.Ask(FindForm(), "Renomear ventoinha", "Nome (inclua CPU, Bomba ou Gabinete para definir o tipo):", c.Display);
            if (r == null) return;
            c.Custom = (r == c.Name) ? "" : r;
            Settings.Set("fan.name." + c.Key, c.Custom);
            it.Text = c.Display; it.SubItems[1].Text = Lang.T(c.Controllable ? c.RoleName : c.RoleName + " (só leitura)");
        }

        void ApplySel()
        {
            List<FanCh> sel = Selected();
            if (sel.Count == 0) { Msg("Selecione na lista pelo menos uma ventoinha controlável.", true); return; }
            int ok = 0; string last = "";
            foreach (FanCh c in sel)
            {
                c.Boost = false; c.Last = -1;
                string m;
                if (optAuto.Checked) { c.Mode = 0; if (c.Auto(out m)) ok++; last = m; }
                else
                {
                    c.Mode = 1; c.Manual = sl.Value;
                    if (c.Set(c.Manual, out m)) { ok++; c.Last = Math.Max(c.MinPct, c.Manual); }
                    last = m;
                }
            }
            Msg(ok + Lang.Pick(" de ", " of ") + sel.Count + " ventoinha(s) aplicada(s). " + last, ok == 0);
        }

        void ByRpm()
        {
            int target = UI.Int(txRpm, 0), ok = 0, nomax = 0;
            foreach (FanCh c in Selected())
            {
                if (c.RpmMax <= 0) { nomax++; continue; }
                int pct = (int)Math.Round(target * 100.0 / c.RpmMax);
                c.Mode = 1; c.Manual = Math.Max(c.MinPct, Math.Min(100, pct)); c.Last = -1; c.Boost = false;
                string m; c.Set(c.Manual, out m); ok++;
            }
            if (ok > 0) optManual.Checked = true;
            Msg(ok + " ajustada(s)." + (nomax > 0 ? " " + nomax + " sem RPM máximo medido: use \"Identificar\" antes." : ""), ok == 0);
        }

        void Identify()
        {
            List<FanCh> sel = Selected();
            if (sel.Count == 0) { Msg("Selecione pelo menos uma ventoinha controlável.", true); return; }
            foreach (FanCh c in sel) { string m; c.Set(100, out m); if (!identList.Contains(c)) identList.Add(c); }
            identUntil = DateTime.UtcNow.AddSeconds(5);
            Msg("Selecionadas a 100% por 5 s. Veja/ouça qual acelerou e acompanhe o RPM na lista.", false);
        }

        void AllAuto()
        {
            foreach (FanCh c in FanHub.All) if (c.Controllable) { c.Mode = 0; c.Boost = false; string m; c.Auto(out m); }
            optAuto.Checked = true;
            Msg("Todas as ventoinhas voltaram ao automático.", false);
        }

        public void RestoreAll()
        {
            if (chkRestore == null || !chkRestore.Checked) return;
            foreach (FanCh c in FanHub.All) if (c.Controllable) { string m; try { c.Auto(out m); } catch (Exception) { } }
        }

        float? TempOf(FanCh c)
        {
            if (c.Gpu != null) { int? t = c.Gpu.Temp(); return t.HasValue ? (float?)t.Value : null; }
            return SensorHub.CpuTemp();
        }

        public void Tick(bool visible)
        {
            DateTime now = DateTime.UtcNow;
            if (identList.Count > 0 && now >= identUntil)
            {
                foreach (FanCh c in identList)
                {
                    int? r = c.RpmNow();
                    if (r.HasValue && r.Value > 0) c.RpmMax = r.Value;
                    string m;
                    if (c.Mode == 1) c.Set(c.Manual, out m); else c.Auto(out m);
                }
                identList.Clear();
                Msg("Identificação concluída: RPM máximo guardado (usado em \"Definir por RPM\").", false);
            }
            bool safeOn = chkSafe.Checked; int safeT = UI.Int(txSafe, 85);
            foreach (FanCh c in FanHub.All)
            {
                if (!c.Controllable || c.Mode != 1 || identList.Contains(c)) continue;
                float? t = TempOf(c);
                if (safeOn && t.HasValue && t.Value >= safeT)
                {
                    if (!c.Boost) { c.Boost = true; string m1; c.Set(100, out m1); Msg("SEGURANÇA: " + c.Display + Lang.Pick(" a 100% (", " at 100% (") + t.Value.ToString("0") + " \u00B0C).", true); }
                    continue;
                }
                if (c.Boost) { if (t.HasValue && t.Value < safeT - 5) { c.Boost = false; c.Last = -1; } else continue; }
                if (c.Last < 0 || Math.Abs(c.Manual - c.Last) >= 1)
                {
                    string m2; c.Set(c.Manual, out m2); c.Last = c.Manual;
                }
            }
            if (visible) UpdateUi();
        }

        static void SetSub(ListViewItem it, int i, string t) { t = Lang.T(t); if (it.SubItems[i].Text != t) it.SubItems[i].Text = t; }

        void UpdateUi()
        {
            foreach (KeyValuePair<FanCh, ListViewItem> kv in items)
            {
                FanCh c = kv.Key; ListViewItem it = kv.Value;
                int? r = c.RpmNow(), p = c.Pct();
                SetSub(it, 3, r.HasValue ? r.Value.ToString() : "-");
                SetSub(it, 4, p.HasValue ? p.Value + "%" : "-");
                SetSub(it, 5, !c.Controllable ? "-" : (c.Boost ? "SEGURANÇA" : (c.Mode == 0 ? "Automático" : "Manual " + c.Manual + "%")));
                Color rc = !c.Controllable ? Xp.Dim : c.Boost ? Xp.Bad : (c.Mode == 1 ? Xp.GroupText : (r.HasValue && r.Value == 0 && p.HasValue && p.Value >= 25 ? Xp.Bad : Xp.Text));   // vermelho = seguranca ou ventoinha parada com potencia
                if (it.ForeColor != rc) it.ForeColor = rc;
            }
        }
    }

    // ------------------------------------------------------------------
    //  Identificacao da CPU, banco de voltagens e utilitarios
    // ------------------------------------------------------------------
    static class CpuId
    {
        public static string Name = "", Vendor = "", Socket = "";
        public static int Cores, Threads, Family, Model;
        static CpuId()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (k != null)
                    {
                        Name = Convert.ToString(k.GetValue("ProcessorNameString")).Trim();
                        string v = Convert.ToString(k.GetValue("VendorIdentifier"));
                        Match fm = Regex.Match(Convert.ToString(k.GetValue("Identifier")), @"Family\s+(\d+)\s+Model\s+(\d+)", RegexOptions.IgnoreCase);
                        if (fm.Success) { Family = int.Parse(fm.Groups[1].Value); Model = int.Parse(fm.Groups[2].Value); }
                        if (v.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0) Vendor = "AMD";
                        else if (v.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) Vendor = "Intel";
                        else Vendor = v;
                    }
                }
            }
            catch (Exception) { }
            Threads = Environment.ProcessorCount;
        }
        public static bool Amd { get { return Vendor == "AMD"; } }

        // AM4 / AM5 / soquete Intel / mobile
        public static string Platform()
        {
            string s = (Socket ?? "").ToUpper();
            if (Amd)
            {
                if (s.Contains("AM5")) return "AM5";
                if (s.Contains("AM4")) return "AM4";
                int i = Name.IndexOf("Ryzen", StringComparison.OrdinalIgnoreCase);
                if (i >= 0)
                {
                    int j = i + 5;
                    while (j < Name.Length && !char.IsDigit(Name[j])) j++;
                    // pula o "3/5/7/9" do nivel e procura o numero do modelo (4 digitos)
                    j++;
                    while (j < Name.Length && !char.IsDigit(Name[j])) j++;
                    if (j < Name.Length)
                    {
                        char d = Name[j];
                        bool mobile = Name.IndexOf(" U", StringComparison.Ordinal) >= 0 || Name.IndexOf("HS", StringComparison.Ordinal) >= 0 ||
                                      Name.IndexOf("HX", StringComparison.Ordinal) >= 0 || Name.EndsWith("H") || Name.IndexOf("Mobile", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (mobile) return "Mobile (soldada)";
                        if (d >= '1' && d <= '5') return "AM4";
                        if (d >= '7' && d <= '9') return "AM5";
                    }
                }
                return s.Length > 0 ? s : "AMD (plataforma não identificada)";
            }
            return s.Length > 0 ? s : (Vendor == "Intel" ? "Intel (soquete não identificado)" : "Desconhecida");
        }
        public static int CoMin { get { return Platform() == "AM5" ? -50 : -30; } }
        public static int CoMax { get { return 30; } }
    }

    static class Proc
    {
        // roda um programa e devolve a saida (stdout + stderr) e o codigo de saida
        public static string Run(string exe, string args, int timeoutMs, out int code)
        {
            code = -1;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception) { } return o + "\n(tempo esgotado)"; }
                    code = p.ExitCode;
                    return o.Trim();
                }
            }
            catch (Exception ex) { return "Erro ao executar " + exe + ": " + ex.Message; }
        }
    }

    // ------------------------------------------------------------------
    //  Memoria RAM: modulos (WMI), canais, voltagem e planilha de timings
    // ------------------------------------------------------------------
    class RamMod
    {
        public string Slot = "", Bank = "", Maker = "", Part = "", Type = "?";
        public long Cap; public int Speed, Conf, Volt, Rank; public bool Ecc;
    }

    static class RamInfo
    {
        static int I(string s) { int v; return int.TryParse(s, out v) ? v : 0; }
        static string TypeName(int smbios)
        {
            switch (smbios)
            {
                case 20: return "DDR"; case 21: return "DDR2"; case 24: return "DDR3"; case 26: return "DDR4"; case 34: return "DDR5";
                case 27: return "LPDDR"; case 28: return "LPDDR2"; case 29: return "LPDDR3"; case 30: return "LPDDR4"; case 35: return "LPDDR5";
                default: return "?";
            }
        }
        public static List<RamMod> Collect()
        {
            List<RamMod> l = new List<RamMod>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
                using (ManagementObjectCollection col = s.Get())
                    foreach (ManagementBaseObject mo in col)
                    {
                        RamMod m = new RamMod();
                        m.Slot = CpuInfo.P(mo, "DeviceLocator") ?? ""; m.Bank = CpuInfo.P(mo, "BankLabel") ?? "";
                        m.Maker = (CpuInfo.P(mo, "Manufacturer") ?? "").Trim(); m.Part = (CpuInfo.P(mo, "PartNumber") ?? "").Trim();
                        long cap; long.TryParse(CpuInfo.P(mo, "Capacity") ?? "0", out cap); m.Cap = cap;
                        m.Speed = I(CpuInfo.P(mo, "Speed")); m.Conf = I(CpuInfo.P(mo, "ConfiguredClockSpeed")); m.Volt = I(CpuInfo.P(mo, "ConfiguredVoltage"));
                        m.Rank = I(CpuInfo.P(mo, "Attributes")) & 0xF;
                        int sm = I(CpuInfo.P(mo, "SMBIOSMemoryType")); if (sm == 0) sm = I(CpuInfo.P(mo, "MemoryType"));
                        m.Type = TypeName(sm);
                        m.Ecc = I(CpuInfo.P(mo, "TotalWidth")) > I(CpuInfo.P(mo, "DataWidth")) && I(CpuInfo.P(mo, "DataWidth")) > 0;
                        l.Add(m); mo.Dispose();
                    }
            }
            catch (Exception) { }
            return l;
        }
        public static int Channels(List<RamMod> mods, out bool estimated)
        {
            estimated = false;
            HashSet<string> ch = new HashSet<string>();
            foreach (RamMod m in mods)
            {
                string c = null, u = (m.Slot + " " + m.Bank).ToUpper();
                int i = u.IndexOf("CHANNEL");
                if (i >= 0)
                {
                    int j = i + 7;
                    while (j < u.Length && u[j] == ' ') j++;
                    if (j < u.Length && char.IsLetterOrDigit(u[j])) c = u[j].ToString();
                }
                if (c == null)
                    for (int k = 0; k + 1 < m.Slot.Length; k++)
                    {
                        char a = char.ToUpper(m.Slot[k]), b = m.Slot[k + 1];
                        if (a >= 'A' && a <= 'H' && char.IsDigit(b) && (k == 0 || !char.IsLetter(m.Slot[k - 1]))) { c = a.ToString(); break; }
                    }
                if (c == null) { estimated = true; break; }
                ch.Add(c);
            }
            if (estimated) return mods.Count >= 2 ? 2 : Math.Max(1, mods.Count);
            return Math.Max(1, ch.Count);
        }
        public static string ChannelText(int n)
        {
            switch (n)
            {
                case 1: return "Single channel"; case 2: return "Dual channel"; case 3: return "Triple channel"; case 4: return "Quad channel";
                case 6: return "Hexa channel"; case 8: return "Octa channel"; default: return n + " canais";
            }
        }
    }

    // ------------------------------------------------------------------
    //  Timings basicos (como CPU-Z / HWiNFO): le direto do controlador de memoria da CPU
    //  usando o driver de baixo nivel (Ring0) que o LibreHardwareMonitor ja carrega.
    //  Valores: tCAS, tRCD, tRP, tRAS, tRC, tRFC e Command Rate.
    // ------------------------------------------------------------------
    static class MemRead
    {
        public class Result
        {
            public Dictionary<string, string> V = new Dictionary<string, string>();
            public string Source = "", Note = "", Kind = "";
        }

        // ---------- AMD Zen: registradores UMC via SMN (porta indice/dado 0x60/0x64 da ponte host) ----------
        static MethodInfo mWr, mRd; static bool inited, okRing;
        static void Init()
        {
            if (inited) return;
            inited = true;
            try
            {
                Type t = typeof(LHM.Computer).Assembly.GetType("LibreHardwareMonitor.Hardware.Ring0");
                if (t == null) return;
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                mWr = t.GetMethod("WritePciConfig", f); mRd = t.GetMethod("ReadPciConfig", f);
                okRing = mWr != null && mRd != null;
            }
            catch (Exception) { }
        }
        // LibreHardwareMonitor novo: modulo PawnIO "AmdFamily17" (metodo ReadSmn), achado por reflexao
        static object pawnObj; static MethodInfo pawnRd; static bool pawnTried;
        static bool PawnSmn(uint addr, out uint val)
        {
            val = 0;
            try
            {
                if (!pawnTried)
                {
                    pawnTried = true;
                    foreach (Type t in typeof(LHM.Computer).Assembly.GetTypes())
                    {
                        if (t.Name.IndexOf("AmdFamily17", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                        MethodInfo m = null;
                        foreach (MethodInfo mi in t.GetMethods(f))
                        {
                            ParameterInfo[] ps = mi.GetParameters();
                            if (mi.Name == "ReadSmn" && ps.Length >= 1 && ps[0].ParameterType == typeof(uint)) { m = mi; break; }
                        }
                        if (m == null) continue;
                        object o = null;
                        if (!m.IsStatic)
                        {
                            ConstructorInfo c = t.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            if (c == null) continue;
                            o = c.Invoke(null);
                        }
                        pawnObj = o; pawnRd = m; break;
                    }
                }
                if (pawnRd == null) return false;
                ParameterInfo[] pp = pawnRd.GetParameters();
                object[] args = new object[pp.Length];
                args[0] = addr;
                for (int i = 1; i < pp.Length; i++) args[i] = pp[i].ParameterType.IsByRef ? (object)0u : (pp[i].ParameterType.IsValueType ? Activator.CreateInstance(pp[i].ParameterType) : null);
                object res = pawnRd.Invoke(pawnObj, args);
                if (pawnRd.ReturnType == typeof(uint)) { val = (uint)res; return true; }
                if (pawnRd.ReturnType == typeof(bool) && (bool)res && pp.Length > 1 && args[1] is uint) { val = (uint)args[1]; return true; }
                return false;
            }
            catch (Exception) { return false; }
        }

        // Acesso direto ao PawnIOLib.dll com o modulo AmdFamily17 embutido na DLL do LibreHardwareMonitor
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DPOpen(out IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DPLoad(IntPtr h, byte[] blob, UIntPtr size);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DPExec(IntPtr h, [MarshalAs(UnmanagedType.LPStr)] string name, long[] inp, UIntPtr inN, long[] outp, UIntPtr outN, out UIntPtr ret);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string n);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr m, string n);
        static IntPtr dpH; static DPExec dpExec; static bool dpTried; public static string DiagWhy = "";
        static bool DirectSmn(uint addr, out uint val)
        {
            val = 0;
            try
            {
                if (!dpTried)
                {
                    dpTried = true;
                    string dll = null;
                    foreach (string e in new string[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetEnvironmentVariable("ProgramFiles") })
                        if (!string.IsNullOrEmpty(e) && File.Exists(Path.Combine(Path.Combine(e, "PawnIO"), "PawnIOLib.dll"))) { dll = Path.Combine(Path.Combine(e, "PawnIO"), "PawnIOLib.dll"); break; }
                    if (dll == null) { DiagWhy += "PawnIOLib.dll não encontrada; "; return false; }
                    Assembly asm = typeof(LHM.Computer).Assembly;
                    byte[] blob = null;
                    foreach (string rn in asm.GetManifestResourceNames())
                        if (rn.EndsWith("AmdFamily17.bin", StringComparison.OrdinalIgnoreCase))
                            using (Stream st = asm.GetManifestResourceStream(rn)) { MemoryStream ms = new MemoryStream(); st.CopyTo(ms); blob = ms.ToArray(); }
                    if (blob == null) { DiagWhy += "a LibreHardwareMonitorLib.dll da pasta libs não traz o módulo PawnIO (DLL antiga: atualize); "; return false; }
                    IntPtr lib = LoadLibraryW(dll);
                    if (lib == IntPtr.Zero) { DiagWhy += "falha ao carregar PawnIOLib.dll; "; return false; }
                    IntPtr po = GetProcAddress(lib, "pawnio_open"), pl = GetProcAddress(lib, "pawnio_load"), px = GetProcAddress(lib, "pawnio_execute");
                    if (po == IntPtr.Zero || pl == IntPtr.Zero || px == IntPtr.Zero) { DiagWhy += "PawnIOLib.dll sem as funções esperadas; "; return false; }
                    DPOpen fo = (DPOpen)Marshal.GetDelegateForFunctionPointer(po, typeof(DPOpen));
                    DPLoad fl = (DPLoad)Marshal.GetDelegateForFunctionPointer(pl, typeof(DPLoad));
                    IntPtr h;
                    int hr = fo(out h);
                    if (hr < 0) { DiagWhy += "pawnio_open falhou (0x" + hr.ToString("X8") + "; rode como administrador); "; return false; }
                    hr = fl(h, blob, (UIntPtr)(uint)blob.Length);
                    if (hr < 0) { DiagWhy += "pawnio_load falhou (0x" + hr.ToString("X8") + "); "; return false; }
                    dpH = h; dpExec = (DPExec)Marshal.GetDelegateForFunctionPointer(px, typeof(DPExec));
                }
                if (dpExec == null) return false;
                long[] o = new long[1]; UIntPtr ret;
                int r = dpExec(dpH, "ioctl_read_smn", new long[] { addr }, (UIntPtr)1u, o, (UIntPtr)1u, out ret);
                if (r < 0) return false;
                val = (uint)o[0];
                return true;
            }
            catch (Exception ex) { DiagWhy += ex.Message + "; "; return false; }
        }

        // ---------- Intel Skylake/Kaby/Coffee/Comet Lake: registradores do controlador (MCHBAR) via modulo PawnIO "IntelMCHBAR" ----------
        // Modulo oficial e assinado do projeto PawnIO.Modules (LGPL-2.1+), so leitura. Os offsets abaixo vem da analise do
        // ASRock Timing Configurator (que usa um driver proprio vulneravel, NAO usado aqui).
        class PawnMod { public IntPtr H; public DPExec Ex; }
        static PawnMod OpenMod(byte[] blob, ref string why)
        {
            string dll = null;
            foreach (string e in new string[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetEnvironmentVariable("ProgramFiles") })
                if (!string.IsNullOrEmpty(e) && File.Exists(Path.Combine(Path.Combine(e, "PawnIO"), "PawnIOLib.dll"))) { dll = Path.Combine(Path.Combine(e, "PawnIO"), "PawnIOLib.dll"); break; }
            if (dll == null) { why += "PawnIOLib.dll não encontrada (instale o PawnIO); "; return null; }
            IntPtr lib = LoadLibraryW(dll);
            if (lib == IntPtr.Zero) { why += "falha ao carregar PawnIOLib.dll; "; return null; }
            IntPtr po = GetProcAddress(lib, "pawnio_open"), pl = GetProcAddress(lib, "pawnio_load"), px = GetProcAddress(lib, "pawnio_execute");
            if (po == IntPtr.Zero || pl == IntPtr.Zero || px == IntPtr.Zero) { why += "PawnIOLib.dll sem as funções esperadas; "; return null; }
            DPOpen fo = (DPOpen)Marshal.GetDelegateForFunctionPointer(po, typeof(DPOpen));
            DPLoad fl = (DPLoad)Marshal.GetDelegateForFunctionPointer(pl, typeof(DPLoad));
            IntPtr h;
            int hr = fo(out h);
            if (hr < 0) { why += "pawnio_open falhou (0x" + hr.ToString("X8") + "; rode como administrador); "; return null; }
            hr = fl(h, blob, (UIntPtr)(uint)blob.Length);
            if (hr < 0) { why += "pawnio_load do módulo falhou (0x" + hr.ToString("X8") + "; CPU não suportada ou MCHBAR desativado); "; return null; }
            PawnMod m = new PawnMod(); m.H = h; m.Ex = (DPExec)Marshal.GetDelegateForFunctionPointer(px, typeof(DPExec));
            return m;
        }

        static byte[] FindBlob(string file)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                foreach (string rn in asm.GetManifestResourceNames())
                    if (rn.EndsWith(file, StringComparison.OrdinalIgnoreCase))
                        using (Stream st = asm.GetManifestResourceStream(rn)) { MemoryStream ms = new MemoryStream(); st.CopyTo(ms); return ms.ToArray(); }
                string b = AppDomain.CurrentDomain.BaseDirectory;
                foreach (string d in new string[] { b, Path.Combine(b, "libs") })
                { string f = Path.Combine(d, file); if (File.Exists(f)) return File.ReadAllBytes(f); }
            }
            catch (Exception) { }
            return null;
        }

        static PawnMod mch; static bool mchTried; public static string MchWhy = "";
        static bool Mch(uint off, out uint val)
        {
            val = 0;
            try
            {
                if (!mchTried)
                {
                    mchTried = true;
                    byte[] blob = FindBlob("IntelMCHBAR.bin");
                    if (blob == null) { MchWhy += "módulo IntelMCHBAR.bin ausente nesta compilação; "; return false; }
                    mch = OpenMod(blob, ref MchWhy);
                }
                if (mch == null) return false;
                long[] o = new long[1]; UIntPtr ret;
                int r = mch.Ex(mch.H, "ioctl_read_dword", new long[] { off }, (UIntPtr)1u, o, (UIntPtr)1u, out ret);
                if (r < 0) return false;
                val = (uint)o[0];
                return true;
            }
            catch (Exception ex) { MchWhy += ex.Message + "; "; return false; }
        }

        static string I(uint v) { return v.ToString(); }

        static Result NativeIntel()
        {
            Result r = new Result();
            int m = CpuId.Model;
            bool blockA = CpuId.Family == 6 && (m == 0x4E || m == 0x5E || m == 0x8E || m == 0x9E || m == 0xA5 || m == 0xA6);
            if (!blockA)
            {
                r.Note = "esta CPU Intel (família " + CpuId.Family + ", modelo 0x" + m.ToString("X") + ") ainda não tem mapa de registradores no programa (suportadas: Skylake, Kaby, Coffee e Comet Lake)";
                return r;
            }
            uint t70 = 0, ch = 0; bool found = false;
            foreach (uint c in new uint[] { 0u, 0x400u })
            {
                uint v;
                if (!Mch(c + 0x4070, out v) || v == 0 || v == 0xFFFFFFFFu) continue;
                uint cl = (v >> 16) & 0x1F;
                if (cl < 4 || cl > 40) continue;
                t70 = v; ch = c; found = true; break;
            }
            if (!found)
            {
                r.Note = "não consegui ler o controlador de memória (MCHBAR): " + (PawnIo.Installed() ? "" : "PawnIO não instalado; ") + MchWhy + "rode como Administrador";
                return r;
            }
            Dictionary<string, string> V = r.V;
            uint tcl = (t70 >> 16) & 0x1F, tcwl = (t70 >> 21) & 0x1F;
            V["tCAS"] = I(tcl); V["tCWL"] = I(tcwl); V["WEODT"] = I((t70 >> 15) & 1); V["tAONPD"] = I((t70 >> 26) & 0x1F);
            uint x;
            uint tras = 0, trp = 0;
            if (Mch(ch + 0x4000, out x) && x != 0 && x != 0xFFFFFFFFu)
            {
                V["tRCD"] = I(x & 0x3F); trp = x & 0x3F; V["tRP"] = I(trp); tras = (x >> 8) & 0x7F; V["tRAS"] = I(tras);
                V["tRTP"] = I((x >> 16) & 0xF); uint wrpre = (x >> 24) & 0x7F; V["tWRPRE"] = I(wrpre);
                if (wrpre > 4 + tcwl) V["tWR"] = I(wrpre - 4 - tcwl);
                V["tRC"] = I(tras + trp);
            }
            if (Mch(ch + 0x4004, out x) && x != 0 && x != 0xFFFFFFFFu) { V["tFAW"] = I(x & 0x3F); V["tRRD_L"] = I((x >> 8) & 0xF); V["tRRD_S"] = I((x >> 12) & 0xF); }
            if (Mch(ch + 0x4008, out x) && x != 0 && x != 0xFFFFFFFFu)
            {
                V["tCKE"] = I(x & 0xF); V["tXP"] = I((x >> 4) & 0xF); V["tXPDLL"] = I((x >> 8) & 0x3F); V["tPRPDEN"] = I((x >> 14) & 3);
                V["tRDPDEN"] = I((x >> 16) & 0x7F); V["tWRPDEN"] = I((x >> 24) & 0x7F);
            }
            string[] sfx = { "sg", "dg", "dr", "dd" };
            string[] grp = { "tRDRD", "tRDWR", "tWRRD", "tWRWR" };
            for (uint g = 0; g < 4; g++)
            {
                if (!Mch(ch + 0x400C + g * 4, out x) || x == 0 || x == 0xFFFFFFFFu) continue;
                for (int b = 0; b < 4; b++)
                {
                    uint mask = (g == 2 && b == 0) ? 0x7Fu : 0x3Fu;
                    V[grp[g] + "_" + sfx[b]] = I((x >> (8 * b)) & mask);
                }
                if (g == 2)
                {
                    uint sg = x & 0x7F, dg = (x >> 8) & 0x3F;
                    if (sg > 6 + tcwl) V["tWTR_L"] = I(sg - 6 - tcwl);
                    if (dg > 6 + tcwl) V["tWTR_S"] = I(dg - 6 - tcwl);
                }
            }
            if (Mch(ch + 0x401C, out x) && x != 0xFFFFFFFFu) { uint c = (x & 0xC) >> 2; V["CR"] = (c == 3 ? 1 : c + 1) + "T"; }
            if (Mch(ch + 0x423C, out x) && x != 0 && x != 0xFFFFFFFFu) { V["tREFI"] = I(x & 0xFFFF); V["tRFC"] = I((x >> 16) & 0x3FF); }
            if (Mch(ch + 0x4238, out x) && x != 0xFFFFFFFFu) { V["OREF_RI"] = I(x & 0xFF); V["tREFIx9"] = I((x >> 25) & 0x7F); }
            if (Mch(ch + 0x4240, out x) && x != 0 && x != 0xFFFFFFFFu)
            { V["tXSDLL"] = I(x & 0xFFF); V["tXS_Offset"] = I((x >> 12) & 0xF); V["tZQOPER"] = I((x >> 16) & 0x3FF); V["tMOD"] = I((x >> 26) & 0x1F); }
            if (Mch(ch + 0x4248, out x) && x != 0 && x != 0xFFFFFFFFu) { V["ZQCS_period"] = I(x & 0x3FF); V["tZQCS"] = I((x >> 10) & 0x3FF); }
            // treino do controlador: RTL / IOL por canal (A = 0x4000.., B = 0x4400..)
            string[] cn = { "A", "B" };
            for (uint k = 0; k < 2; k++)
            {
                uint w;
                if (!Mch(0x5004 + k * 4, out w) || (w & 0xFFFF) == 0 || w == 0xFFFFFFFFu) continue;
                if (Mch(0x4020 + k * 0x400, out x) && x != 0xFFFFFFFFu) { V["RTL_" + cn[k] + "0"] = I(x & 0x7F); V["RTL_" + cn[k] + "1"] = I((x >> 16) & 0x7F); }
                if (Mch(0x4024 + k * 0x400, out x) && x != 0xFFFFFFFFu)
                {
                    V["IOL_" + cn[k] + "0"] = I(x & 0xF); V["IOL_" + cn[k] + "1"] = I((x >> 8) & 0xF);
                    if (k == 0) { V["IOL_off"] = I((x >> 16) & 0x7F); V["RFR_delay"] = I((x >> 24) & 0x7F); }
                }
            }
            r.Kind = "Intel";
            r.Source = "controlador de memória da CPU (Intel, canal " + (ch == 0 ? "A" : "B") + ", via PawnIO IntelMCHBAR)";
            return r;
        }

        static bool Smn(uint addr, out uint val)
        {
            val = 0;
            Init();
            if (okRing && RingSmn(addr, out val)) return true;
            if (PawnSmn(addr, out val)) return true;
            return DirectSmn(addr, out val);
        }
        static bool RingSmn(uint addr, out uint val)
        {
            val = 0;
            System.Threading.Mutex m = null; bool got = false;
            try
            {
                m = new System.Threading.Mutex(false, "Global\\Access_PCI");
                got = m.WaitOne(500);
                if (!got) return false;
                object r1 = mWr.Invoke(null, new object[] { 0u, 0x60u, addr });
                object[] a = new object[] { 0u, 0x64u, 0u };
                object r2 = mRd.Invoke(null, a);
                if (!(r1 is bool) || !(r2 is bool) || !(bool)r1 || !(bool)r2) return false;
                val = (uint)a[2];
                return true;
            }
            catch (Exception) { return false; }
            finally { if (got && m != null) { try { m.ReleaseMutex(); } catch (Exception) { } } if (m != null) m.Close(); }
        }

        static bool T1(uint r, out int cl, out int ras, out int rcd)
        {
            cl = (int)(r & 0x3F); ras = (int)((r >> 8) & 0x7F); rcd = (int)((r >> 16) & 0x3F); int wr = (int)((r >> 24) & 0x3F);
            return (r & 0xC0) == 0 && cl >= 6 && cl <= 70 && rcd >= 6 && rcd <= 70 && wr >= 6 && wr <= 70 && ras >= 15 && ras <= 140 && ras >= rcd;
        }

        // ---------- AMD Ryzen (Zen 2/3/4/5, DDR4 e DDR5): mapa completo dos registradores UMC lidos via SMN ----------
        // Formato: endereco(hex, relativo ao UMC) chave bitAlto/bitBaixo em DDR4 e bitAlto/bitBaixo em DDR5 ("-" = nao existe).
        // Os enderecos e campos seguem a documentacao publica dos registradores (PPR da AMD), como catalogados pelo ZenTimings/ZenStates-Core.
        static readonly string[] AmdMap = {
            "100 ECC 12 12 12 12", "100 BurstLength 9 8 9 8",
            "12C AggrPD 30 30 30 30", "12C PowerDown 28 28 28 28",
            "200 Preamble2t 12 12 - -", "200 GDM 11 11 18 18", "200 Cmd2T 10 10 17 17", "200 BankGroupEn 8 8 16 16",
            "204 tRCDWR 29 24 29 24", "204 tRCD 21 16 21 16", "204 tRAS 14 8 14 8", "204 tCAS 5 0 5 0",
            "208 tRC 7 0 7 0", "208 tRP 21 16 21 16", "208 tRPpb - - 29 24", "208 tRCpb - - 15 8",
            "20C tRTP 28 24 28 24", "20C tRRD_L 12 8 12 8", "20C tRRD_S 4 0 4 0",
            "210 tFAW 6 0 7 0",
            "214 tWTR_L 22 16 22 16", "214 tWTR_S 12 8 12 8", "214 tCWL 5 0 5 0",
            "218 tWR 6 0 7 0",
            "21C TRCPAGE 31 20 31 20", "21C tPPD - - 2 0",
            "220 RDRDBan 31 30 31 30", "220 tRDRDSCL 27 24 29 24", "220 tRDRDSC 19 16 19 16", "220 tRDRDSD 11 8 11 8", "220 tRDRDDD 3 0 3 0",
            "224 WRWRBan 31 30 31 30", "224 tWRWRSCL 29 24 29 24", "224 tWRWRSC 19 16 19 16", "224 tWRWRSD 11 8 11 8", "224 tWRWRDD 3 0 3 0",
            "228 tMW - - 28 24", "228 tRDWR 12 8 13 8", "228 tWRRD 3 0 3 0",
            "22C ZqcsInterval 29 20 29 20", "22C tZQOPER 19 8 19 8", "22C tZQCS 7 0 7 0",
            "230 tREFI 15 0 15 0",
            "234 tMODPDA 29 24 29 24", "234 tMRDPDA 21 16 21 16", "234 tMOD 13 8 13 8", "234 tMRD 5 0 5 0",
            "238 tDLLK 26 16 27 16", "238 tXS 10 0 11 0",
            "240 tMRRI - - 26 24", "240 tMRR - - 21 16", "240 tMRW - - 13 8",
            "244 AggrPwrDownDly 30 25 31 26", "244 PwrDownDly 24 17 19 8", "244 tPD 4 0 4 0",
            "248 tSRX2SRX - - 15 10", "248 tSTAB - - 9 0",
            "250 tSTAG 23 16 26 16", "250 tSTAGsb - - 8 0",
            "254 tCKE 28 24 28 24", "254 tCPDED 19 16 20 16", "254 tXP 5 0 5 0",
            "258 tPHYWRD 26 24 26 24", "258 tPHYRDL 21 16 23 16", "258 tPHYWRL 12 8 15 8", "258 RDDATAEN 6 0 6 0",
            "25C LpExitDly 13 8 13 8", "25C LpDly 5 0 5 0",
            "28C CmdStgCnt 21 11 21 11", "28C RcvrWait 10 0 10 0",
            "2A4 tWRPRE 10 8 10 8", "2A4 tRDPRE 2 0 2 0",
            "2A8 tECSc - - 8 0" };
        static readonly HashSet<string> AmdBool = new HashSet<string> { "ECC", "AggrPD", "PowerDown", "GDM", "BankGroupEn", "Preamble2t" };

        static Result NativeAmdFull()
        {
            Result r = new Result();
            Init();
            Dictionary<uint, uint> cache = new Dictionary<uint, uint>();
            uint off = 0; bool five = false, found = false;
            for (int pass = 0; pass < 2 && !found; pass++)
                for (uint i = 0; i < 8 && !found; i++)
                {
                    uint o = i << 20, r200, r204, df0, d0, d1;
                    if (!Smn(o + 0x50200u, out r200) || !Smn(o + 0x50204u, out r204)) continue;
                    if (r200 == 0 || r200 == 0xFFFFFFFFu || r204 == 0 || r204 == 0xFFFFFFFFu) continue;
                    if (pass == 0)
                    {
                        Smn(o + 0x50DF0u, out df0); Smn(o + 0x50000u, out d0); Smn(o + 0x50008u, out d1);
                        if (((df0 >> 19) & 1) != 0 || ((d0 & 1) == 0 && (d1 & 1) == 0)) continue;   // canal desativado ou sem pente
                    }
                    uint cl = r204 & 0x3F, rcd = (r204 >> 16) & 0x3F, ras = (r204 >> 8) & 0x7F;
                    if (cl < 6 || rcd < 4 || ras < rcd || ras > 127) continue;
                    off = o; five = (r200 & 0xFFFF) >= 1000; found = true;
                    cache[0x200] = r200;
                }
            if (!found) return r;
            Dictionary<string, string> V = r.V;
            foreach (string line in AmdMap)
            {
                string[] f = line.Split(' ');
                int hi = five ? (f[4] == "-" ? -1 : int.Parse(f[4])) : (f[2] == "-" ? -1 : int.Parse(f[2]));
                int lo = five ? (f[5] == "-" ? -1 : int.Parse(f[5])) : (f[3] == "-" ? -1 : int.Parse(f[3]));
                if (hi < 0) continue;
                uint a = Convert.ToUInt32(f[0], 16), reg;
                if (!cache.TryGetValue(a, out reg))
                {
                    if (!Smn(off + 0x50000u + a, out reg)) reg = 0xFFFFFFFFu;
                    cache[a] = reg;
                }
                if (reg == 0xFFFFFFFFu) continue;
                uint v = (reg >> lo) & (uint)((1UL << (hi - lo + 1)) - 1);
                string k = f[1], t;
                if (AmdBool.Contains(k)) t = v == 1 ? Lang.T("Ativado") : (v == 0 ? Lang.T("Desativado") : "-");
                else if (k == "Cmd2T") { k = "CR"; t = v == 1 ? "2T" : "1T"; }
                else if (k == "BurstLength") t = "BL" + (2 << (int)Math.Min(v, 3u));
                else if (k == "tWRPRE") t = I(v + 1);
                else if (k == "tRDPRE") t = I(v < 2 ? v + 1 : v);
                else t = I(v);
                V[k] = t;
            }
            // tRFC: registradores 0x50260.. (DDR4: 3 campos no mesmo registrador; DDR5: pares)
            uint[] rf = new uint[4];
            for (uint q = 0; q < 4; q++) if (!Smn(off + 0x50260u + q * 4, out rf[q])) rf[q] = 0;
            if (!five)
            {
                uint t0 = rf[0], t1 = rf[1], reg = t0 != t1 ? (t0 != 0x21060138u ? t0 : t1) : t0;
                if (reg != 0) { V["tRFC"] = I(reg & 0x7FF); V["tRFC2"] = I((reg >> 11) & 0x7FF); V["tRFC4"] = I((reg >> 22) & 0x3FF); }
            }
            else
            {
                foreach (uint reg in rf)
                    if (reg > 0 && reg != 0x00C00138u) { V["tRFC"] = I(reg & 0xFFFF); V["tRFC2"] = I(reg >> 16); break; }
                for (uint q = 0; q < 4; q++)
                {
                    uint x;
                    if (Smn(off + 0x502C0u + q * 4, out x) && (x & 0x7FF) != 0) { V["tRFCsb"] = I(x & 0x7FF); break; }
                }
            }
            if (!V.ContainsKey("tRC") && V.ContainsKey("tRAS") && V.ContainsKey("tRP")) V["tRC"] = I(uint.Parse(V["tRAS"]) + uint.Parse(V["tRP"]));
            // velocidade (BCLK 100 MHz): DDR4 = razao/3 x 200, DDR5 = valor x 2
            uint rr = cache[0x200];
            V["MTs"] = five ? I((rr & 0xFFFF) * 2) : ((rr & 0x7F) / 3.0 * 200.0).ToString("0");
            // troca de grupos de banco
            uint b0, b1, a0, a1;
            if (Smn(off + 0x50050u, out b0) && Smn(off + 0x50058u, out b1)) V["BGS"] = (b0 == 0x87654321u && b1 == 0x87654321u) ? Lang.T("Desativado") : Lang.T("Ativado");
            if (Smn(off + 0x500D0u, out a0) && Smn(off + 0x500D4u, out a1)) V["BGSAlt"] = (((a0 >> 4) & 0x7F) > 0 || ((a1 >> 4) & 0x7F) > 0) ? Lang.T("Ativado") : Lang.T("Desativado");
            if (!V.ContainsKey("tCAS") || !V.ContainsKey("tRCD")) { V.Clear(); return r; }
            r.Kind = five ? "AMD5" : "AMD4";
            r.Source = "controlador de memória da CPU (AMD Ryzen, " + (five ? "DDR5" : "DDR4") + ", canal " + (char)('A' + (int)(off >> 20)) + ")";
            return r;
        }

        static Result Native()
        {
            Result r = new Result();
            if (CpuId.Vendor == "Intel") return NativeIntel();
            if (!CpuId.Amd) { r.Note = "leitura direta do controlador: só CPUs AMD Ryzen e Intel nesta versão"; return r; }
            Init();
            try { Result full = NativeAmdFull(); if (full.V.Count >= 8) return full; } catch (Exception) { }
            bool anyRead = false;
            for (int ch = 0; ch < 4; ch++)
            {
                uint off = (uint)ch * 0x100000u;
                uint[] R = new uint[20]; bool[] ok = new bool[20]; int cnt = 0;
                for (int i = 0; i < 20; i++) { ok[i] = Smn(off + 0x50200u + (uint)(i * 4), out R[i]); if (ok[i]) cnt++; }
                if (cnt == 0) continue;
                anyRead = true;
                // procura o registrador de tCL/tRAS/tRCD (a posicao varia entre geracoes/AGESA)
                int i1 = -1, cl = 0, ras = 0, rcd = 0;
                for (int i = 0; i < 20 && i1 < 0; i++) if (ok[i] && T1(R[i], out cl, out ras, out rcd)) i1 = i;
                if (i1 < 0) continue;
                r.V["tCAS"] = cl.ToString(); r.V["tRCD"] = rcd.ToString(); r.V["tRAS"] = ras.ToString();
                for (int k = 0; k < 20; k++)
                {
                    int j = (i1 + 1 + k) % 20;
                    if (j == i1 || !ok[j]) continue;
                    int c = (int)(R[j] & 0xFF), p = (int)((R[j] >> 16) & 0x3F);
                    if (p >= 6 && p <= 70 && c >= ras + p - 3 && c <= ras + p + 8) { r.V["tRP"] = p.ToString(); r.V["tRC"] = c.ToString(); break; }
                }
                uint[] rf = { 0x50260u, 0x50264u, 0x50268u, 0x5025Cu, 0x50270u };
                foreach (uint ra in rf)
                {
                    uint v;
                    if (!Smn(off + ra, out v)) continue;
                    int f1 = (int)(v & 0x7FF), f2 = (int)((v >> 11) & 0x7FF), f3 = (int)((v >> 22) & 0x3FF);
                    if (f1 >= 80 && f1 <= 1500 && f2 >= 40 && f2 <= f1 && f3 >= 20 && f3 <= f2) { r.V["tRFC"] = f1.ToString(); break; }
                }
                uint cfg;
                if (Smn(off + 0x50100u, out cfg)) r.V["CR"] = ((cfg >> 10) & 1) != 0 ? "2T" : "1T";
                r.Source = "controlador de memória da CPU (AMD Ryzen, canal " + (char)('A' + ch) + ")";
                return r;
            }
            r.Note = anyRead ? "registradores do controlador não reconhecidos nesta CPU (use o botão Diagnóstico)"
                : "não foi possível ler o controlador de memória: " + (PawnIo.Installed() ? "" : "PawnIO não instalado; ") + DiagWhy + "rode como Administrador";
            return r;
        }

        // ---------- Plano B: relatorio do CPU-Z, se o CPU-Z estiver na pasta do programa ----------
        static string FindCpuz()
        {
            string b = AppDomain.CurrentDomain.BaseDirectory;
            string saved = Settings.Get("cpuzpath", "");
            if (saved.Length > 0 && File.Exists(saved)) return saved;
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            List<string> dirs = new List<string> { b, Path.Combine(b, "tools"), Path.Combine(b, "cpuz"), Path.Combine(b, "tools\\cpuz"), Path.Combine(b, "libs"),
                Path.Combine(pf, "CPUID\\CPU-Z"), Path.Combine(pf86, "CPUID\\CPU-Z"), desk, dl };
            string[] exes = { "cpuz_x64.exe", "cpuz.exe", "cpuz_x32.exe" };
            foreach (string d in dirs) foreach (string e in exes) { string p = Path.Combine(d, e); if (File.Exists(p)) return p; }
            // pastas "cpu-z*" dentro de Downloads/Desktop (versao portatil extraida)
            foreach (string root in new string[] { desk, dl })
                try
                {
                    foreach (string sub in Directory.GetDirectories(root, "*cpu*z*"))
                        foreach (string e in exes) { string p = Path.Combine(sub, e); if (File.Exists(p)) return p; }
                }
                catch (Exception) { }
            return null;
        }

        static Result FromCpuz()
        {
            Result r = new Result();
            string exe = FindCpuz();
            if (exe == null) return r;
            string tmp = Path.Combine(Path.GetTempPath(), "bunos_cpuz_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            int code;
            Proc.Run(exe, "-txt=" + tmp, 40000, out code);
            string f = tmp + ".txt";
            if (!File.Exists(f)) return r;
            string text = File.ReadAllText(f);
            try { File.Delete(f); } catch (Exception) { }
            string[] lines = text.Replace("\r", "").Split('\n');
            int from = 0, to = lines.Length;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].IndexOf("Memory Timings", StringComparison.OrdinalIgnoreCase) >= 0) { from = i; to = Math.Min(lines.Length, i + 26); break; }
            string[][] syn = {
                new string[] { "tCAS", @"CAS#?\s*Latency", @"\bTcas\b" }, new string[] { "tRCD", @"RAS#?\s*to\s*CAS#?", @"\bTrcd\b" },
                new string[] { "tRP", @"RAS#?\s*Precharge", @"\bTrp\b" }, new string[] { "tRAS", @"Cycle\s*Time\s*\(tRAS\)", @"\bTras\b" },
                new string[] { "tRC", @"Bank\s*Cycle\s*Time", @"\bTrc\b" }, new string[] { "tRFC", @"Row\s*Refresh\s*Cycle", @"\bTrfc\b" },
                new string[] { "CR", @"Command\s*Rate" } };
            for (int i = from; i < to; i++)
                foreach (string[] sy in syn)
                {
                    if (r.V.ContainsKey(sy[0])) continue;
                    for (int k = 1; k < sy.Length; k++)
                    {
                        Match m = Regex.Match(lines[i], sy[k], RegexOptions.IgnoreCase);
                        if (!m.Success) continue;
                        Match n = Regex.Match(lines[i].Substring(m.Index + m.Length), @"(\d+(?:[.,]\d+)?)\s*(T)?\b");
                        if (!n.Success) continue;
                        string v = n.Groups[1].Value.Replace(',', '.');
                        if (v.EndsWith(".0")) v = v.Substring(0, v.Length - 2);
                        if (sy[0] == "CR") v += "T";
                        r.V[sy[0]] = v; break;
                    }
                }
            r.Source = "relatório do CPU-Z (" + Path.GetFileName(exe) + ")";
            return r;
        }

        // Tenta o controlador de memoria; se nao der, o CPU-Z (se existir)
        public static string Diagnose()
        {
            StringBuilder sb = new StringBuilder();
            Init();
            sb.AppendLine("CPU: " + CpuId.Vendor);
            sb.AppendLine("PawnIO instalado: " + PawnIo.Installed());
            sb.AppendLine("Ring0 (LHM) com PCI: " + okRing);
            sb.AppendLine("CPU família " + CpuId.Family + " modelo 0x" + CpuId.Model.ToString("X") + "  módulo IntelMCHBAR.bin embutido: " + (FindBlob("IntelMCHBAR.bin") != null));
            if (CpuId.Vendor == "Intel")
            {
                foreach (uint a in new uint[] { 0x4000, 0x4004, 0x4008, 0x400C, 0x4010, 0x4014, 0x4018, 0x401C, 0x4070, 0x4238, 0x423C, 0x4240, 0x4248, 0x4020, 0x4024, 0x5004, 0x5008 })
                { uint v; sb.AppendLine("MCHBAR+0x" + a.ToString("X") + ": " + (Mch(a, out v) ? "0x" + v.ToString("X8") : "falhou")); }
                sb.AppendLine("Motivos MCHBAR: " + MchWhy);
            }
            try
            {
                Assembly asm = typeof(LHM.Computer).Assembly;
                sb.AppendLine("LibreHardwareMonitorLib: " + asm.GetName().Version);
                int n = 0;
                foreach (Type t in asm.GetTypes())
                    if (t.Name.IndexOf("Pawn", StringComparison.OrdinalIgnoreCase) >= 0 || t.Name.IndexOf("AmdFamily", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (n++ > 14) break;
                        StringBuilder ms = new StringBuilder();
                        foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                            if (mi.Name.IndexOf("Smn", StringComparison.OrdinalIgnoreCase) >= 0) ms.Append(mi.Name + " ");
                        sb.AppendLine("  tipo " + t.FullName + (ms.Length > 0 ? "  [" + ms + "]" : ""));
                    }
                if (n == 0) sb.AppendLine("  nenhum tipo PawnIO na DLL (DLL antiga: atualize a LibreHardwareMonitorLib.dll)");
                n = 0;
                foreach (string rn in asm.GetManifestResourceNames())
                    if (rn.IndexOf("Pawn", StringComparison.OrdinalIgnoreCase) >= 0) { if (n++ > 10) break; sb.AppendLine("  recurso " + rn); }
            }
            catch (Exception ex) { sb.AppendLine("erro ao inspecionar a DLL: " + ex.Message); }
            uint[] addrs = { 0x50200, 0x50204, 0x50208, 0x5020C, 0x50210, 0x50214, 0x50218, 0x5021C, 0x50220, 0x50224, 0x50228, 0x5025C, 0x50260, 0x50264, 0x50268, 0x50100, 0x50230, 0x50000, 0x50008, 0x50DF0 };
            foreach (uint a in addrs)
            {
                uint v1 = 0, v2 = 0, v3 = 0;
                bool o1 = okRing && RingSmn(a, out v1), o2 = PawnSmn(a, out v2), o3 = DirectSmn(a, out v3);
                sb.AppendLine("SMN 0x" + a.ToString("X") + ": " + (o2 ? "0x" + v2.ToString("X8") : (o3 ? "0x" + v3.ToString("X8") : (o1 ? "0x" + v1.ToString("X8") : "falhou"))));
            }
            sb.AppendLine("Motivos: " + DiagWhy);
            return sb.ToString();
        }

        public static Result Read()
        {
            Result n;
            try { n = Native(); } catch (Exception ex) { n = new Result(); n.Note = ex.Message; }
            if (n.V.Count >= 6) return n;
            Result z = null;
            try { z = FromCpuz(); } catch (Exception) { }
            if (z != null && z.V.Count > n.V.Count) return z;
            if (n.V.Count > 0) return n;
            if (n.Note.Length == 0) n.Note = "esta plataforma (" + CpuId.Vendor + ") não expõe os timings sem um driver próprio" + (FindCpuz() == null ? " e o CPU-Z não foi encontrado" : " e o CPU-Z não retornou dados");
            return n;
        }
    }

    // ------------------------------------------------------------------
    //  Pagina de memoria RAM: resumo + modulos + timings basicos
    // ------------------------------------------------------------------
    class RamPage : Panel
    {
        Label lblType, lblCap, lblSpeed, lblChan, lblVolt, lblRank, lblBw, lblSrc, lblLat;
        Dictionary<string, Label> tl = new Dictionary<string, Label>();
        // "#titulo" abre uma secao; os demais sao pares chave / nome. Chave sem leitura fica "-".
        static readonly string[] SpecIntel = {
            "#", "Principais",
            "tCAS", "tCL (CAS):", "tRCD", "tRCD:", "tRP", "tRP:", "tRAS", "tRAS:", "tRC", "tRC:", "tRFC", "tRFC:", "CR", "Command Rate:", "tCWL", "tCWL:", "tFAW", "tFAW:",
            "#", "Ativação e escrita",
            "tRRD_S", "tRRD_S:", "tRRD_L", "tRRD_L:", "tWR", "tWR:", "tRTP", "tRTP:", "tWTR_S", "tWTR_S:", "tWTR_L", "tWTR_L:", "tWRPRE", "tWRPRE:",
            "#", "Refresh e calibração",
            "tREFI", "tREFI:", "tREFIx9", "tREFIx9:", "OREF_RI", "OREF_RI:", "tXSDLL", "tXSDLL:", "tXS_Offset", "tXS Offset:", "tZQOPER", "tZQOPER:",
            "tZQCS", "tZQCS:", "ZQCS_period", "ZQCS period:", "tMOD", "tMOD:",
            "#", "Leitura / escrita entre bancos e ranks",
            "tRDRD_sg", "tRDRD_sg:", "tRDRD_dg", "tRDRD_dg:", "tRDRD_dr", "tRDRD_dr:", "tRDRD_dd", "tRDRD_dd:",
            "tRDWR_sg", "tRDWR_sg:", "tRDWR_dg", "tRDWR_dg:", "tRDWR_dr", "tRDWR_dr:", "tRDWR_dd", "tRDWR_dd:",
            "tWRRD_sg", "tWRRD_sg:", "tWRRD_dg", "tWRRD_dg:", "tWRRD_dr", "tWRRD_dr:", "tWRRD_dd", "tWRRD_dd:",
            "tWRWR_sg", "tWRWR_sg:", "tWRWR_dg", "tWRWR_dg:", "tWRWR_dr", "tWRWR_dr:", "tWRWR_dd", "tWRWR_dd:",
            "#", "Economia de energia",
            "tCKE", "tCKE:", "tXP", "tXP:", "tXPDLL", "tXPDLL:", "tPRPDEN", "tPRPDEN:", "tRDPDEN", "tRDPDEN:", "tWRPDEN", "tWRPDEN:", "tAONPD", "tAONPD:", "WEODT", "Write Early ODT:",
            "#", "Treino do controlador (RTL / IOL)",
            "RTL_A0", "RTL canal A / DIMM0:", "RTL_A1", "RTL canal A / DIMM1:", "RTL_B0", "RTL canal B / DIMM0:", "RTL_B1", "RTL canal B / DIMM1:",
            "IOL_A0", "IOL canal A / DIMM0:", "IOL_A1", "IOL canal A / DIMM1:", "IOL_B0", "IOL canal B / DIMM0:", "IOL_B1", "IOL canal B / DIMM1:",
            "IOL_off", "IOL offset:", "RFR_delay", "RFR delay:" };
        static readonly string[] SpecBasic = {
            "#", "Principais",
            "tCAS", "tCL (CAS):", "tRCD", "tRCD:", "tRP", "tRP:", "tRAS", "tRAS:", "tRC", "tRC:", "tRFC", "tRFC:", "CR", "Command Rate:" };
        // prefixo "4|" = so DDR4, "5|" = so DDR5
        static readonly string[] SpecAmd = {
            "#", "Principais",
            "MTs", "Velocidade (MT/s):", "tCAS", "tCL (CAS):", "tRCD", "tRCDRD:", "tRCDWR", "tRCDWR:", "tRP", "tRP:", "tRAS", "tRAS:", "tRC", "tRC:", "tRFC", "tRFC:", "CR", "Command Rate:",
            "tCWL", "tCWL:", "tFAW", "tFAW:", "GDM", "Gear Down Mode:", "BGS", "Bank Group Swap:", "BGSAlt", "BGS Alt:", "ECC", "ECC:", "BurstLength", "Burst length:",
            "#", "Ativação e escrita",
            "tRRD_S", "tRRD_S:", "tRRD_L", "tRRD_L:", "tWR", "tWR:", "tRTP", "tRTP:", "tWTR_S", "tWTR_S:", "tWTR_L", "tWTR_L:", "tWRPRE", "tWRPRE:", "tRDPRE", "tRDPRE:",
            "5|tRPpb", "tRPpb:", "5|tRCpb", "tRCpb:", "5|tPPD", "tPPD:", "TRCPAGE", "tRCPAGE:", "5|tMW", "tMW:", "4|Preamble2t", "Preamble 2T:",
            "#", "Refresh e calibração",
            "tREFI", "tREFI:", "tRFC2", "tRFC2:", "4|tRFC4", "tRFC4:", "5|tRFCsb", "tRFCsb:", "tSTAG", "tSTAG:", "5|tSTAGsb", "tSTAGsb:", "tXS", "tXS:", "tDLLK", "tDLLK:",
            "tMOD", "tMOD:", "tMODPDA", "tMODPDA:", "tMRD", "tMRD:", "tMRDPDA", "tMRDPDA:", "tZQOPER", "tZQOPER:", "tZQCS", "tZQCS:", "ZqcsInterval", "ZQCS interval:",
            "5|tMRR", "tMRR:", "5|tMRW", "tMRW:", "5|tMRRI", "tMRRI:", "5|tSRX2SRX", "tSRX2SRX:", "5|tSTAB", "tSTAB:", "5|tECSc", "tECSc:",
            "#", "Leitura / escrita entre bancos e ranks",
            "tRDRDSCL", "tRDRDSCL:", "tRDRDSC", "tRDRDSC:", "tRDRDSD", "tRDRDSD:", "tRDRDDD", "tRDRDDD:",
            "tWRWRSCL", "tWRWRSCL:", "tWRWRSC", "tWRWRSC:", "tWRWRSD", "tWRWRSD:", "tWRWRDD", "tWRWRDD:",
            "tRDWR", "tRDWR:", "tWRRD", "tWRRD:", "RDRDBan", "RDRD Ban:", "WRWRBan", "WRWR Ban:",
            "#", "Economia de energia",
            "tCKE", "tCKE:", "tXP", "tXP:", "tPD", "tPD:", "tCPDED", "tCPDED:", "PowerDown", "Power Down:", "AggrPD", "Aggressive PD:",
            "AggrPwrDownDly", "Aggr PD delay:", "PwrDownDly", "PD delay:", "LpExitDly", "LP exit delay:", "LpDly", "LP delay:",
            "#", "Interface física (PHY)",
            "tPHYWRD", "tPHYWRD:", "tPHYWRL", "tPHYWRL:", "tPHYRDL", "tPHYRDL:", "RDDATAEN", "RdDataEn:", "CmdStgCnt", "CmdStgCnt:", "RcvrWait", "RcvrWait:" };
        string[] curSpec; string lastKind = "";
        XpGroup grpTim;
        static string[] FilterSpec(string[] spec, string kind)
        {
            List<string> o = new List<string>();
            for (int i = 0; i < spec.Length; i += 2)
            {
                string k = spec[i];
                if (k.Length > 2 && k[1] == '|') { if ((k[0] == '5') != (kind == "AMD5")) continue; k = k.Substring(2); }
                o.Add(k); o.Add(spec[i + 1]);
            }
            return o.ToArray();
        }
        static string[] SpecFor(string kind)
        {
            if (kind == "Intel") return SpecIntel;
            if (kind == "AMD4" || kind == "AMD5") return FilterSpec(SpecAmd, kind);
            return SpecBasic;
        }
        ListView lvMod = new XpList();
        int speed, channels = 1;
        bool reading, readOnce;
        Dictionary<string, string> tim = new Dictionary<string, string>();

        public RamPage()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            BuildLatency(); BuildSummary(); BuildMods(); BuildTimings(SpecBasic);
            VisibleChanged += delegate { if (Visible && !readOnce) { readOnce = true; ReadAsync(); } };
        }

        Label Val(Control g, string cap, int x, int y, int w)
        {
            Label c = UI.Lbl(g, cap, x, y);
            Label v = UI.Lbl(g, "-", x + 128, y, Xp.UiBold, Xp.GroupText);
            v.AutoSize = false; v.AutoEllipsis = true; v.SetBounds(x + 128, y, w, 18);
            return v;
        }

        // ---------- latencia de memoria (estilo AIDA64): ponteiro encadeado aleatorio, de 4 KB a 256 MB ----------
        LatGraph latGraph = new LatGraph(); Label lblLatBig, lblLatSub, lblLatSteps; XpProgress pbLat; XpButton bLat; bool latBusy, latStop;
        public static int LatSink;

        void BuildLatency()
        {
            XpGroup g = new XpGroup(); g.Title = "Latência da memória (ns)"; g.SetBounds(0, 0, 656, 142);
            lblLatBig = UI.Lbl(g, "- ns", 14, 28, Xp.Big, Xp.GroupText);
            lblLatBig.Font = new Font(Xp.Big.FontFamily, 22f, FontStyle.Bold);
            lblLatSub = UI.Lbl(g, "Latência da RAM (bloco de 256 MB)", 14, 66, Xp.Ui, Xp.Dim);
            lblLatSub.AutoSize = false; lblLatSub.SetBounds(14, 66, 200, 32);
            bLat = UI.Btn(g, "Medir latência", 14, 102, 130, delegate { ToggleLat(); });
            bLat.Height = 28;
            pbLat = new XpProgress(); pbLat.SetBounds(150, 108, 64, 14); g.Controls.Add(pbLat);
            latGraph.SetBounds(222, 24, 424, 82); g.Controls.Add(latGraph);
            lblLatSteps = UI.Lbl(g, "", 222, 108, Xp.Ui, Xp.Dim);
            lblLatSteps.AutoSize = false; lblLatSteps.SetBounds(222, 108, 424, 30);
            Controls.Add(g);
        }

        void ToggleLat()
        {
            if (latBusy) { latStop = true; return; }
            latBusy = true; latStop = false;
            bLat.Text = "Parar"; latGraph.Kb.Clear(); latGraph.Ns.Clear(); latGraph.Invalidate();
            lblLatBig.Text = "..."; lblLatSteps.Text = "Medindo (feche outros programas para um resultado melhor)..."; pbLat.Value = 0;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { LatRun(); });
        }

        void LatRun()
        {
            Th.Thread me = Th.Thread.CurrentThread;
            Th.ThreadPriority old = me.Priority;
            string err = null;
            IntPtr oldMask = IntPtr.Zero;
            bool pinned = false;
            try
            {
                me.Priority = Th.ThreadPriority.Highest;
                // fixa em um nucleo (fora do 0, que atende interrupcoes) para o resultado nao oscilar
                int cpus = Environment.ProcessorCount;
                if (cpus >= 2 && cpus <= 64)
                {
                    Th.Thread.BeginThreadAffinity();
                    oldMask = SetThreadAffinityMask(GetCurrentThread(), (IntPtr)(1L << (cpus >= 4 ? 2 : 1)));
                    pinned = true;
                }
                long[] kbs = { 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144 };
                for (int s = 0; s < kbs.Length && !latStop; s++)
                {
                    double ns = LatMeasure(kbs[s] * 1024L);
                    double kb = kbs[s], v = ns; int pct = (s + 1) * 100 / kbs.Length;
                    try { BeginInvoke(new Action(delegate { latGraph.Kb.Add(kb); latGraph.Ns.Add(v); latGraph.Invalidate(); pbLat.Value = pct; lblLatBig.Text = v.ToString("0.0") + " ns"; })); } catch (Exception) { }
                }
            }
            catch (Exception ex) { err = ex.Message; }
            if (pinned) { try { SetThreadAffinityMask(GetCurrentThread(), oldMask); Th.Thread.EndThreadAffinity(); } catch (Exception) { } }
            me.Priority = old;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    latBusy = false; bLat.Text = "Medir latência"; pbLat.Value = 0;
                    int n = latGraph.Ns.Count;
                    if (n == 0) { lblLatBig.Text = "- ns"; lblLatSteps.Text = err != null ? "Falhou: " + err : "Cancelado."; return; }
                    lblLatBig.Text = latGraph.Ns[n - 1].ToString("0.0") + " ns";
                    lblLatSub.Text = "Latência da RAM (bloco de " + (latGraph.Kb[n - 1] >= 1024 ? (latGraph.Kb[n - 1] / 1024).ToString("0") + " MB" : latGraph.Kb[n - 1].ToString("0") + " KB") + ")";
                    StringBuilder sb = new StringBuilder();
                    double[] want = { 16, 256, 8192 };
                    string[] nm = { "16 KB", "256 KB", "8 MB" };
                    for (int i = 0; i < want.Length; i++) { int k = latGraph.Kb.IndexOf(want[i]); if (k >= 0) sb.Append(nm[i] + ": " + latGraph.Ns[k].ToString("0.0") + " ns    "); }
                    lblLatSteps.Text = sb.ToString() + (latStop ? "(interrompido)" : "") + (err != null ? " " + err : "");
                }));
            }
            catch (Exception) { }
        }

        [DllImport("kernel32.dll")] static extern IntPtr SetThreadAffinityMask(IntPtr h, IntPtr mask);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();

        // Latencia como descrita na documentacao do AIDA64: um unico nucleo/thread, leituras dependentes (o endereco da proxima
        // leitura vem do dado da anterior), paginas de 4 KB, acesso ALEATORIO com passo minimo de 128 bytes (evita o efeito do
        // prefetcher da linha adjacente) e "block-random": salta aleatoriamente dentro de um bloco menor que a janela do TLB
        // (limita a penalidade de falha de TLB), percorre o bloco e so entao passa para outro bloco do buffer.
        // Mede o tempo medio (ns) de uma leitura em um buffer de 'bytes'.
        const int LatBlock = 2 * 1024 * 1024;   // bloco: 512 paginas de 4 KB (ajustado por medicao contra o AIDA64)
        static double LatMeasure(long bytes)
        {
            int slots = (int)(bytes / 128);                       // posicoes de 128 B
            int blockSlots = (int)(Math.Min(bytes, (long)LatBlock) / 128);
            int blocks = Math.Max(1, slots / blockSlots);
            int[] a = new int[bytes / 4];                         // cada posicao guarda o indice (em ints) da proxima
            int[] seq = new int[slots];
            int[] bo = new int[blocks];
            for (int i = 0; i < blocks; i++) bo[i] = i;
            Random rnd = new Random(12345);
            for (int i = blocks - 1; i > 0; i--) { int j = rnd.Next(i + 1); int tmp = bo[i]; bo[i] = bo[j]; bo[j] = tmp; }
            int[] pos = new int[blockSlots];
            int n = 0;
            for (int q = 0; q < blocks; q++)
            {
                for (int i = 0; i < blockSlots; i++) pos[i] = i;
                for (int i = blockSlots - 1; i > 0; i--) { int j = rnd.Next(i + 1); int tmp = pos[i]; pos[i] = pos[j]; pos[j] = tmp; }
                for (int i = 0; i < blockSlots; i++) seq[n++] = bo[q] * blockSlots + pos[i];
            }
            for (int i = 0; i < slots; i++) a[seq[i] * 32] = seq[(i + 1) % slots] * 32;   // um unico ciclo (32 ints = 128 B)
            seq = null;
            long steps = Math.Max(4000000L, slots * 2L);
            int p = 0;
            for (long i = 0; i < slots; i++) p = a[p];            // aquecimento: uma volta completa (traz o buffer para o cache/TLB se couber)
            double best = double.MaxValue;
            int reps = bytes <= 8L * 1024 * 1024 ? 3 : 2;
            for (int r = 0; r < reps; r++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                for (long i = 0; i < steps; i++) p = a[p];
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / steps);
            }
            LatSink += p;
            return best;
        }

        void BuildSummary()
        {
            XpGroup g = new XpGroup(); g.Title = "Resumo da memória"; g.SetBounds(0, 148, 656, 120);
            lblType = Val(g, "Tipo:", 14, 24, 190); lblCap = Val(g, "Capacidade total:", 14, 46, 190); lblSpeed = Val(g, "Velocidade:", 14, 68, 190);
            lblChan = Val(g, "Canais:", 350, 24, 180); lblVolt = Val(g, "Voltagem:", 350, 46, 180); lblRank = Val(g, "Ranks / ECC:", 350, 68, 180);
            lblBw = UI.Lbl(g, "", 14, 92, Xp.Ui, Xp.Dim);
            lblBw.AutoSize = false; lblBw.AutoEllipsis = true; lblBw.SetBounds(14, 92, 630, 20);
            Controls.Add(g);
        }

        void BuildMods()
        {
            ((XpList)lvMod).AutoFit = true; lvMod.SetBounds(0, 274, 656, 104); lvMod.View = View.Details; lvMod.FullRowSelect = true; lvMod.GridLines = true;
            lvMod.BackColor = Xp.EditBg; lvMod.Font = Xp.Ui;
            lvMod.Columns.Add("Slot", 140); lvMod.Columns.Add("Cap.", 55); lvMod.Columns.Add("Fabricante", 90); lvMod.Columns.Add("Part number", 150);
            lvMod.Columns.Add("MT/s", 50); lvMod.Columns.Add("Volt", 55); lvMod.Columns.Add("Tipo", 50); lvMod.Columns.Add("Ranks", 50);
            lvMod.Items.Add(new ListViewItem(new string[] { "Lendo módulos...", "", "", "", "", "", "", "" }));
            Controls.Add(lvMod);
        }

        void BuildTimings(string[] TSpec)
        {
            curSpec = TSpec; tl.Clear();
            if (grpTim != null) { Controls.Remove(grpTim); grpTim.Dispose(); }
            XpGroup g = new XpGroup(); g.Title = "Timings atuais (leitura automática)"; grpTim = g;
            int y = 26;
            int pos = 0;
            while (pos < TSpec.Length)
            {
                y += 4;
                UI.Lbl(g, TSpec[pos + 1], 14, y, Xp.UiBold);
                y += 22; pos += 2;
                int start = pos;
                while (pos < TSpec.Length && TSpec[pos] != "#") pos += 2;
                int n = (pos - start) / 2, rows = (n + 2) / 3;   // ordem vertical: preenche a coluna 1, depois a 2, depois a 3
                for (int k = 0; k < n; k++)
                {
                    int col = k / rows, row = k % rows, x = 14 + col * 214, yy = y + row * 20;
                    UI.Lbl(g, TSpec[start + k * 2 + 1], x, yy);
                    Label v = UI.Lbl(g, "-", x + 112, yy, Xp.UiBold, Xp.GroupText);
                    v.AutoSize = false; v.AutoEllipsis = true; v.SetBounds(x + 112, yy, 96, 18);
                    tl[TSpec[start + k * 2]] = v;
                }
                y += rows * 20;
            }
            lblLat = UI.Lbl(g, "", 14, y + 6, Xp.Ui, Xp.Dim);
            lblLat.AutoSize = false; lblLat.AutoEllipsis = true; lblLat.SetBounds(14, y + 6, 630, 20);
            lblSrc = UI.Lbl(g, "Lendo os timings...", 14, y + 28, Xp.Ui, Xp.Dim);
            lblSrc.AutoSize = false; lblSrc.SetBounds(14, y + 28, 630, 36);
            int by = y + 68;
            UI.Btn(g, "Ler novamente", 14, by, 110, delegate { ReadAsync(); });
            UI.Btn(g, "Diagnóstico", 130, by, 100, delegate
            {
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string d;
                    try { d = MemRead.Diagnose(); } catch (Exception ex) { d = ex.ToString(); }
                    try { BeginInvoke(new Action(delegate { try { Clipboard.SetText(d); } catch (Exception) { } Dlg.Show(this, d + "\n(copiado para a área de transferência)", "BunoTunner - diagnóstico dos timings"); })); } catch (Exception) { }
                });
            });
            g.SetBounds(0, 384, 656, by + 44);
            Controls.Add(g);
        }

        public void ReadAsync()
        {
            if (reading) return;
            reading = true;
            lblSrc.ForeColor = Xp.Dim; lblSrc.Text = "Lendo os timings direto do controlador de memória...";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                MemRead.Result r;
                try { r = MemRead.Read(); } catch (Exception ex) { r = new MemRead.Result(); r.Note = ex.Message; }
                try { BeginInvoke(new Action(delegate { ShowRead(r); })); } catch (Exception) { }
            });
        }

        void ShowRead(MemRead.Result r)
        {
            reading = false;
            tim = r.V;
            string[] want = SpecFor(r.Kind);
            if (r.Kind != lastKind) BuildTimings(want);
            lastKind = r.Kind;
            foreach (KeyValuePair<string, Label> kv in tl)
            {
                string v; tim.TryGetValue(kv.Key, out v);
                kv.Value.Text = v == null ? "-" : v;
            }
            if (r.V.Count > 0) { lblSrc.ForeColor = Xp.Good; lblSrc.Text = "Fonte: " + r.Source + "."; }
            else { lblSrc.ForeColor = Xp.Bad; lblSrc.Text = "Não consegui ler: " + r.Note + ". Clique em Diagnóstico e envie o texto."; }
            Recalc();
        }

        void Recalc()
        {
            string v; double cl;
            if (speed > 0 && tim.TryGetValue("tCAS", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out cl))
                lblLat.Text = "Latência real do CAS: " + (cl * 2000.0 / speed).ToString("0.00") + " ns  (tCAS x 2000 / MT/s)";
            else lblLat.Text = "";
        }

        // ---------- modulos ----------
        public void LoadAsync()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<RamMod> mods = RamInfo.Collect();
                try { BeginInvoke(new Action(delegate { ShowMods(mods); })); } catch (Exception) { }
            });
        }

        void ShowMods(List<RamMod> mods)
        {
            lvMod.Items.Clear();
            if (mods.Count == 0)
            {
                lvMod.Items.Add(new ListViewItem(new string[] { "Nenhum módulo informado pelo Windows (WMI)", "", "", "", "", "", "", "" }));
                return;
            }
            long total = 0; int maxConf = 0, maxSpd = 0, volt = 0, ranks = 0; bool ecc = false; string type = mods[0].Type;
            foreach (RamMod m in mods)
            {
                total += m.Cap; maxConf = Math.Max(maxConf, m.Conf); maxSpd = Math.Max(maxSpd, m.Speed); if (m.Volt > 0) volt = m.Volt; ranks += m.Rank; ecc |= m.Ecc;
                lvMod.Items.Add(new ListViewItem(new string[] { m.Slot + (m.Bank.Length > 0 ? "  (" + m.Bank + ")" : ""), (m.Cap >> 30) + " GB", m.Maker, m.Part,
                    (m.Conf > 0 ? m.Conf : m.Speed).ToString(), m.Volt > 0 ? (m.Volt / 1000.0).ToString("0.00") + " V" : "-", m.Type, m.Rank > 0 ? m.Rank.ToString() : "?" }));
            }
            bool est; channels = RamInfo.Channels(mods, out est);
            speed = maxConf > 0 ? maxConf : maxSpd;
            lblType.Text = type + "  (" + mods.Count + " módulo(s))";
            lblCap.Text = (total >> 30) + " GB";
            lblSpeed.Text = speed + " MT/s" + (maxSpd > 0 && maxSpd != speed ? "  (nominal " + maxSpd + ")" : "") + "  =  " + (speed / 2) + " MHz reais";
            lblChan.Text = RamInfo.ChannelText(channels) + (est ? "  (estimado)" : "");
            lblVolt.Text = volt > 0 ? (volt / 1000.0).ToString("0.000") + " V" : "não informado pela BIOS";
            lblRank.Text = (ranks > 0 ? ranks + " rank(s) no total" : "ranks ?") + "  /  ECC: " + (ecc ? "sim" : "não");
            lblBw.Text = "Banda teórica: " + (speed * 8.0 * channels / 1000.0).ToString("0.0") + " GB/s  (MT/s x 8 bytes x " + channels + " canal(ais)).  " +
                         (type == "DDR5" ? "DDR5: cada módulo tem 2 subcanais de 32 bits." : "");
            Recalc();
        }
    }

    // ------------------------------------------------------------------
    //  Afinidade de processos (quais nucleos cada programa pode usar)
    // ------------------------------------------------------------------
    class ProcRow { public int Pid; public string Name = "", Prio = "-"; public double Cpu; public long Mem, Mask = -1; }
    class AffPreset { public string Name; public long Mask; public int Prio; }
    class AffRule { public string Proc, Preset; }

    class ProcSorter : System.Collections.IComparer
    {
        int col; bool asc;
        public ProcSorter(int c, bool a) { col = c; asc = a; }
        public int Compare(object x, object y)
        {
            ProcRow p = (ProcRow)((ListViewItem)x).Tag, q = (ProcRow)((ListViewItem)y).Tag;
            int r;
            switch (col)
            {
                case 1: r = p.Pid.CompareTo(q.Pid); break;
                case 2: r = p.Cpu.CompareTo(q.Cpu); break;
                case 3: r = p.Mem.CompareTo(q.Mem); break;
                case 4: r = p.Mask.CompareTo(q.Mask); break;
                case 5: r = string.Compare(p.Prio, q.Prio, StringComparison.OrdinalIgnoreCase); break;
                default: r = string.Compare(p.Name, q.Name, StringComparison.OrdinalIgnoreCase); break;
            }
            return asc ? r : -r;
        }
    }

    class AffinityPage : Panel
    {
        static readonly ProcessPriorityClass[] PrioMap = { ProcessPriorityClass.Normal, ProcessPriorityClass.Idle, ProcessPriorityClass.BelowNormal, ProcessPriorityClass.Normal, ProcessPriorityClass.AboveNormal, ProcessPriorityClass.High };
        static readonly string[] Blocked = { "system", "idle", "registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon", "memory compression", "secure system" };

        int N;
        ListView lvP = new XpList(), lvRules = new XpList();
        XpText txFilter; XpOption chkPause, chkSame, chkRules; XpOption[] cpuBox;
        ComboBox cbPrio, cbPre; Label lblCount, lblMsg;
        Dictionary<int, ListViewItem> pidItem = new Dictionary<int, ListViewItem>();
        List<ProcRow> lastRows = new List<ProcRow>();
        Dictionary<int, TimeSpan> prevCpu = new Dictionary<int, TimeSpan>(); DateTime prevT = DateTime.UtcNow;
        List<AffPreset> presets = new List<AffPreset>(); List<AffRule> rules = new List<AffRule>();
        HashSet<int> applied = new HashSet<int>();
        object lk = new object();
        volatile string pendingLog;
        bool busy, loadingPre; int sortCol; bool sortAsc = true;
        Timer timer = new Timer();
        LTip tip = new LTip();

        public AffinityPage()
        {
            Dock = DockStyle.Fill;
            N = Math.Max(1, Math.Min(62, Environment.ProcessorCount));
            tip.InitialDelay = 300; tip.AutoPopDelay = 9000;
            UI.Lbl(this, "Filtrar:", 0, 6, Xp.UiBold);
            txFilter = UI.Txt(this, 50, 2, 150, "");
            txFilter.Box.TextChanged += delegate { pidItem.Clear(); lvP.Items.Clear(); Fill(lastRows); };
            UI.Btn(this, "Atualizar", 208, 1, 70, delegate { Refresh2(); });
            UI.Btn(this, "Marcar mesmo nome", 284, 1, 130, delegate { MarkSameName(); });
            UI.Btn(this, "Desmarcar", 420, 1, 80, delegate { foreach (ListViewItem it in lvP.Items) it.Checked = false; });
            chkPause = UI.Opt(this, "Pausar atualização", 510, 4, 160, false);

            lvP.SetBounds(0, 30, 676, 226); lvP.View = View.Details; lvP.CheckBoxes = true; lvP.FullRowSelect = true; lvP.GridLines = true;
            lvP.MultiSelect = true; lvP.HideSelection = false; lvP.BackColor = Xp.EditBg; lvP.Font = Xp.Ui;
            lvP.Columns.Add("Processo", 170); lvP.Columns.Add("PID", 55); lvP.Columns.Add("CPU", 55); lvP.Columns.Add("Memória", 75);
            lvP.Columns.Add("Afinidade (CPUs)", 170); lvP.Columns.Add("Prioridade", 110);
            UI.DoubleBuffer(lvP);
            lvP.ColumnClick += delegate(object o, ColumnClickEventArgs e)
            {
                if (sortCol == e.Column) sortAsc = !sortAsc; else { sortCol = e.Column; sortAsc = e.Column == 0; }
                lvP.ListViewItemSorter = new ProcSorter(sortCol, sortAsc); lvP.Sort();
            };
            lvP.ListViewItemSorter = new ProcSorter(0, true);
            Controls.Add(lvP);
            tip.SetToolTip(lvP, "Marque (caixinha) os processos que quer alterar. Sem nenhum marcado, vale a seleção com Ctrl/Shift.");

            BuildCores(); BuildPresets(); BuildRules();
            LoadPresets(); LoadRules();
            timer.Interval = 2500;
            timer.Tick += delegate { if ((Visible && !chkPause.Checked) || chkRules.Checked) Refresh2(); };
            timer.Start();
            VisibleChanged += delegate { if (Visible) Refresh2(); };
        }

        public void Shutdown() { timer.Stop(); }

        // ---------- interface ----------
        void BuildCores()
        {
            XpGroup g = new XpGroup(); g.Title = "Núcleos (CPUs lógicos) que os processos marcados podem usar"; g.SetBounds(0, 262, 420, 324);
            Panel gp = new Panel(); gp.SetBounds(10, 22, 400, 108); gp.AutoScroll = true; g.Controls.Add(gp);
            cpuBox = new XpOption[N];
            for (int i = 0; i < N; i++)
            {
                XpOption o = new XpOption(); o.Text = "CPU " + i; o.SetBounds((i % 6) * 64, (i / 6) * 22, 62, 18); o.Checked = true;
                o.CheckedChanged += delegate { UpdateCount(); };
                gp.Controls.Add(o); cpuBox[i] = o;
            }
            UI.Btn(g, "Todos", 10, 136, 60, delegate { SetMask((1L << N) - 1); });
            UI.Btn(g, "Nenhum", 74, 136, 60, delegate { SetMask(0); });
            UI.Btn(g, "Físicos", 138, 136, 64, delegate { SetMask(PhysMask(0)); });
            UI.Btn(g, "2a thread", 206, 136, 64, delegate { SetMask(PhysMask(1)); });
            UI.Btn(g, "1a metade", 274, 136, 64, delegate { SetMask(((1L << (N / 2)) - 1)); });
            UI.Btn(g, "2a metade", 342, 136, 64, delegate { SetMask(((1L << N) - 1) & ~((1L << (N / 2)) - 1)); });
            UI.Btn(g, "Inverter", 10, 162, 60, delegate { SetMask(~GridMask() & ((1L << N) - 1)); });
            lblCount = UI.Lbl(g, "", 80, 166, Xp.UiBold, Xp.GroupText);
            UI.Lbl(g, "Prioridade:", 10, 198);
            cbPrio = new ComboBox(); cbPrio.DropDownStyle = ComboBoxStyle.DropDownList; cbPrio.Font = Xp.Ui; cbPrio.SetBounds(84, 194, 170, 22);
            cbPrio.Items.AddRange(new object[] { "Manter a atual", "Baixa (Idle)", "Abaixo do normal", "Normal", "Acima do normal", "Alta" });
            cbPrio.SelectedIndex = 0; g.Controls.Add(cbPrio);
            chkSame = UI.Opt(g, "Aplicar também a TODOS os processos com o mesmo nome", 10, 222, 390, false);
            UI.Btn(g, "Aplicar aos marcados", 10, 250, 150, delegate { ApplyNow(false); }).Height = 26;
            UI.Btn(g, "Restaurar padrão", 168, 250, 130, delegate { ApplyNow(true); }).Height = 26;
            lblMsg = UI.Lbl(g, "", 10, 284, Xp.UiBold, Xp.Good);
            lblMsg.AutoSize = false; lblMsg.SetBounds(10, 282, 400, 36);
            tip.SetToolTip(chkSame, "Navegadores e jogos criam vários processos com o mesmo nome; isto altera todos de uma vez.");
            Controls.Add(g);
            UpdateCount();
        }

        void BuildPresets()
        {
            XpGroup g = new XpGroup(); g.Title = "Presets"; g.SetBounds(428, 262, 248, 150);
            cbPre = new ComboBox(); cbPre.DropDownStyle = ComboBoxStyle.DropDownList; cbPre.Font = Xp.Ui; cbPre.SetBounds(10, 24, 228, 22); g.Controls.Add(cbPre);
            cbPre.SelectedIndexChanged += delegate { PresetToGrid(); };
            UI.Btn(g, "Salvar atual", 10, 54, 100, delegate { SavePreset(); });
            UI.Btn(g, "Excluir", 116, 54, 70, delegate { DeletePreset(); });
            Label h = UI.Lbl(g, "Escolha um preset para carregar núcleos e prioridade; depois clique em \"Aplicar aos marcados\". \"Salvar atual\" guarda o que está na grade.", 10, 84, Xp.Ui, Xp.Dim);
            h.MaximumSize = new Size(230, 0);
            Controls.Add(g);
        }

        void BuildRules()
        {
            XpGroup g = new XpGroup(); g.Title = "Regras automáticas"; g.SetBounds(428, 418, 248, 168);
            chkRules = UI.Opt(g, "Aplicar regras automaticamente", 10, 22, 228, false);
            ((XpList)lvRules).AutoFit = true; lvRules.SetBounds(10, 46, 228, 80); lvRules.View = View.Details; lvRules.FullRowSelect = true; lvRules.GridLines = true; lvRules.Font = Xp.Ui; lvRules.BackColor = Xp.EditBg;
            lvRules.Columns.Add("Processo", 110); lvRules.Columns.Add("Preset", 100);
            g.Controls.Add(lvRules);
            UI.Btn(g, "Nova regra", 10, 132, 100, delegate { NewRule(); });
            UI.Btn(g, "Remover", 116, 132, 80, delegate { RemoveRule(); });
            chkRules.CheckedChanged += delegate { Settings.Set("aff.rulesOn", chkRules.Checked ? "1" : "0"); };
            tip.SetToolTip(chkRules, "Quando o processo abrir, o preset é aplicado sozinho (ex.: jogo.exe -> núcleos físicos).");
            Controls.Add(g);
        }

        // ---------- grade de nucleos ----------
        long GridMask() { long m = 0; for (int i = 0; i < N; i++) if (cpuBox[i].Checked) m |= 1L << i; return m; }
        void SetMask(long m) { for (int i = 0; i < N; i++) cpuBox[i].Checked = ((m >> i) & 1) != 0; UpdateCount(); }
        long PhysMask(int off)
        {
            bool smt = CpuId.Threads > CpuId.Cores && CpuId.Cores > 0;
            if (!smt) return off == 0 ? (1L << N) - 1 : 0;
            long m = 0; for (int i = off; i < N; i += 2) m |= 1L << i;
            return m;
        }
        void UpdateCount() { int c = 0; for (int i = 0; i < N; i++) if (cpuBox[i].Checked) c++; lblCount.Text = c + Lang.Pick(" de ", " of ") + N + " CPUs marcados"; }

        string MaskText(long mask)
        {
            if (mask == -1) return "(negado)";
            long all = (1L << N) - 1;
            if ((mask & all) == all) return "Todos (" + N + ")";
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < N)
            {
                if (((mask >> i) & 1) != 0)
                {
                    int j = i;
                    while (j + 1 < N && ((mask >> (j + 1)) & 1) != 0) j++;
                    if (sb.Length > 0) sb.Append(",");
                    sb.Append(i == j ? i.ToString() : i + "-" + j);
                    i = j + 1;
                }
                else i++;
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        void Msg(string t, bool err) { lblMsg.ForeColor = err ? Xp.Bad : Xp.Good; lblMsg.Text = t; }

        // ---------- lista de processos ----------
        void Refresh2()
        {
            if (busy) return;
            busy = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { Collect(); } catch (Exception) { }
                finally { busy = false; }
            });
        }

        void Collect()
        {
            List<ProcRow> rows = new List<ProcRow>();
            DateTime now = DateTime.UtcNow;
            double wall = (now - prevT).TotalSeconds; if (wall <= 0.2) wall = 1;
            Dictionary<int, TimeSpan> cur = new Dictionary<int, TimeSpan>();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    ProcRow r = new ProcRow(); r.Pid = p.Id; r.Name = p.ProcessName;
                    try { r.Mem = p.WorkingSet64; } catch (Exception) { }
                    try { r.Mask = (long)p.ProcessorAffinity; } catch (Exception) { r.Mask = -1; }
                    try { r.Prio = p.PriorityClass.ToString(); } catch (Exception) { r.Prio = "-"; }
                    try
                    {
                        TimeSpan t = p.TotalProcessorTime; cur[r.Pid] = t; TimeSpan pt;
                        if (prevCpu.TryGetValue(r.Pid, out pt)) r.Cpu = (t - pt).TotalSeconds / wall / Environment.ProcessorCount * 100.0;
                    }
                    catch (Exception) { }
                    rows.Add(r);
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            prevCpu = cur; prevT = now;
            if (chkRulesOn) ApplyRules(rows);
            if (Visible) { try { BeginInvoke(new Action(delegate { Fill(rows); })); } catch (Exception) { } }
        }
        bool chkRulesOn { get { return Settings.Get("aff.rulesOn", "0") == "1"; } }

        void Fill(List<ProcRow> rows)
        {
            lastRows = rows;
            string f = txFilter.Text.Trim().ToLower();
            HashSet<int> alive = new HashSet<int>();
            lvP.BeginUpdate();
            foreach (ProcRow r in rows)
            {
                if (f.Length > 0 && r.Name.ToLower().IndexOf(f) < 0) continue;
                alive.Add(r.Pid);
                string[] t = { r.Name, r.Pid.ToString(), r.Cpu.ToString("0.0") + "%", (r.Mem >> 20) + " MB", Lang.T(MaskText(r.Mask)), Lang.T(r.Prio) };
                ListViewItem it;
                if (!pidItem.TryGetValue(r.Pid, out it)) { it = new ListViewItem(t); it.Tag = r; pidItem[r.Pid] = it; lvP.Items.Add(it); }
                else
                {
                    it.Tag = r;
                    for (int i = 0; i < t.Length; i++) if (it.SubItems[i].Text != t[i]) it.SubItems[i].Text = t[i];
                }
            }
            List<int> gone = new List<int>();
            foreach (KeyValuePair<int, ListViewItem> kv in pidItem) if (!alive.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int pid in gone) { lvP.Items.Remove(pidItem[pid]); pidItem.Remove(pid); }
            lvP.EndUpdate();
            lvP.Sort();
            string log = pendingLog;
            if (log != null) { pendingLog = null; Msg(log, false); }
        }

        void MarkSameName()
        {
            HashSet<string> names = new HashSet<string>();
            foreach (ListViewItem it in lvP.SelectedItems) names.Add(((ProcRow)it.Tag).Name.ToLower());
            foreach (ListViewItem it in lvP.CheckedItems) names.Add(((ProcRow)it.Tag).Name.ToLower());
            foreach (ListViewItem it in lvP.Items) if (names.Contains(((ProcRow)it.Tag).Name.ToLower())) it.Checked = true;
        }

        // ---------- aplicar ----------
        static bool IsBlocked(string name) { return Array.IndexOf(Blocked, name.ToLower()) >= 0; }

        static bool ApplyTo(int pid, long mask, int prioIdx, out string err)
        {
            err = null;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    p.ProcessorAffinity = (IntPtr)mask;
                    if (prioIdx > 0) p.PriorityClass = PrioMap[prioIdx];
                }
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        List<ProcRow> Targets()
        {
            List<ProcRow> l = new List<ProcRow>();
            if (lvP.CheckedItems.Count > 0) foreach (ListViewItem it in lvP.CheckedItems) l.Add((ProcRow)it.Tag);
            else foreach (ListViewItem it in lvP.SelectedItems) l.Add((ProcRow)it.Tag);
            if (chkSame.Checked)
            {
                HashSet<string> names = new HashSet<string>(); HashSet<int> have = new HashSet<int>();
                foreach (ProcRow r in l) { names.Add(r.Name.ToLower()); have.Add(r.Pid); }
                foreach (ProcRow r in lastRows) if (names.Contains(r.Name.ToLower()) && have.Add(r.Pid)) l.Add(r);
            }
            return l;
        }

        void ApplyNow(bool restore)
        {
            long mask = restore ? (1L << N) - 1 : GridMask();
            int prio = restore ? 3 : cbPrio.SelectedIndex;
            if (mask == 0) { Msg("Marque pelo menos um CPU na grade.", true); return; }
            List<ProcRow> t = Targets();
            if (t.Count == 0) { Msg("Marque (caixinha) ou selecione pelo menos um processo na lista.", true); return; }
            int ok = 0, skip = 0, fail = 0; string err = "";
            foreach (ProcRow r in t)
            {
                if (IsBlocked(r.Name)) { skip++; continue; }
                string e;
                if (ApplyTo(r.Pid, mask, prio, out e)) ok++; else { fail++; if (err.Length == 0) err = r.Name + ": " + e; }
            }
            Msg(ok + " processo(s) alterado(s)" + (skip > 0 ? ", " + skip + " do sistema ignorado(s)" : "") + (fail > 0 ? ", " + fail + " falhou (" + err + ")" : "") + ".", ok == 0);
            Refresh2();
        }

        // ---------- presets ----------
        void LoadPresets()
        {
            presets.Clear();
            foreach (string e in Settings.Get("aff.presets", "").Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = e.Split(new string[] { "~=~" }, StringSplitOptions.None);
                long m; int pr;
                if (p.Length == 3 && long.TryParse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out m) && int.TryParse(p[2], out pr))
                { AffPreset a = new AffPreset(); a.Name = p[0]; a.Mask = m; a.Prio = pr; presets.Add(a); }
            }
            RefreshPresetCombo(null);
        }
        void SavePresets()
        {
            List<string> l = new List<string>();
            foreach (AffPreset a in presets) l.Add(a.Name + "~=~" + a.Mask.ToString("X") + "~=~" + a.Prio);
            Settings.Set("aff.presets", string.Join("~|~", l.ToArray()));
        }
        void RefreshPresetCombo(string select)
        {
            loadingPre = true;
            cbPre.Items.Clear();
            foreach (AffPreset a in presets) cbPre.Items.Add(a.Name);
            if (select != null) cbPre.SelectedItem = select;
            loadingPre = false;
        }
        void PresetToGrid()
        {
            if (loadingPre || cbPre.SelectedItem == null) return;
            foreach (AffPreset a in presets)
                if (a.Name == (string)cbPre.SelectedItem) { SetMask(a.Mask); cbPrio.SelectedIndex = Math.Max(0, Math.Min(5, a.Prio)); }
        }
        void SavePreset()
        {
            long m = GridMask();
            if (m == 0) { Msg("Marque pelo menos um CPU antes de salvar.", true); return; }
            string n = PromptForm.Ask(FindForm(), "Preset", "Nome do preset (ex.: Jogo - núcleos físicos):", cbPre.Text);
            if (string.IsNullOrEmpty(n)) return;
            AffPreset ex = null; foreach (AffPreset a in presets) if (a.Name == n) ex = a;
            if (ex == null) { ex = new AffPreset(); ex.Name = n; presets.Add(ex); }
            ex.Mask = m; ex.Prio = cbPrio.SelectedIndex;
            SavePresets(); RefreshPresetCombo(n);
            Msg("Preset \"" + n + "\" salvo.", false);
        }
        void DeletePreset()
        {
            if (cbPre.SelectedItem == null) return;
            string n = (string)cbPre.SelectedItem;
            presets.RemoveAll(delegate(AffPreset a) { return a.Name == n; });
            SavePresets(); RefreshPresetCombo(null);
        }

        // ---------- regras automaticas ----------
        void LoadRules()
        {
            lock (lk)
            {
                rules.Clear();
                foreach (string e in Settings.Get("aff.rules", "").Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] p = e.Split(new string[] { "~=~" }, StringSplitOptions.None);
                    if (p.Length == 2) { AffRule r = new AffRule(); r.Proc = p[0]; r.Preset = p[1]; rules.Add(r); }
                }
            }
            chkRules.Checked = chkRulesOn;
            ShowRules();
        }
        void ShowRules()
        {
            lvRules.Items.Clear();
            lock (lk) foreach (AffRule r in rules) lvRules.Items.Add(new ListViewItem(new string[] { r.Proc, r.Preset }));
        }
        void SaveRules()
        {
            List<string> l = new List<string>();
            lock (lk) foreach (AffRule r in rules) l.Add(r.Proc + "~=~" + r.Preset);
            Settings.Set("aff.rules", string.Join("~|~", l.ToArray()));
        }
        void NewRule()
        {
            if (cbPre.SelectedItem == null) { Msg("Escolha (ou salve) um preset antes de criar a regra.", true); return; }
            string def = lvP.SelectedItems.Count > 0 ? ((ProcRow)lvP.SelectedItems[0].Tag).Name + ".exe" : "";
            string n = PromptForm.Ask(FindForm(), "Nova regra", "Nome do processo (ex.: game.exe). Preset: " + cbPre.SelectedItem, def);
            if (string.IsNullOrEmpty(n)) return;
            AffRule r = new AffRule(); r.Proc = n.Trim(); r.Preset = (string)cbPre.SelectedItem;
            lock (lk) rules.Add(r);
            SaveRules(); ShowRules();
        }
        void RemoveRule()
        {
            if (lvRules.SelectedItems.Count == 0) return;
            int i = lvRules.SelectedIndices[0];
            lock (lk) if (i < rules.Count) rules.RemoveAt(i);
            SaveRules(); ShowRules();
        }
        void ApplyRules(List<ProcRow> rows)
        {
            AffRule[] rs; AffPreset[] ps;
            lock (lk) { rs = rules.ToArray(); ps = presets.ToArray(); }
            if (rs.Length == 0) return;
            HashSet<int> alive = new HashSet<int>();
            foreach (ProcRow r in rows) alive.Add(r.Pid);
            applied.RemoveWhere(delegate(int pid) { return !alive.Contains(pid); });
            foreach (ProcRow r in rows)
            {
                if (applied.Contains(r.Pid) || IsBlocked(r.Name)) continue;
                foreach (AffRule rule in rs)
                {
                    string pn = rule.Proc.ToLower(); if (pn.EndsWith(".exe")) pn = pn.Substring(0, pn.Length - 4);
                    if (pn != r.Name.ToLower()) continue;
                    foreach (AffPreset a in ps)
                        if (a.Name == rule.Preset)
                        {
                            string e;
                            if (ApplyTo(r.Pid, a.Mask & ((1L << N) - 1), a.Prio, out e)) pendingLog = "Regra aplicada: " + r.Name + " (PID " + r.Pid + ") -> " + a.Name;
                            applied.Add(r.Pid);
                        }
                }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Helpers de interface
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    //  Desinstalador: remove o programa e os restos (pastas, registro, inicializacao, servicos, tarefas)
    // ------------------------------------------------------------------
    class UProg
    {
        public string Name = "", Publisher = "", Version = "", Loc = "", Uninst = "", KeyFull = "", Date = "";
        public long SizeKb;
        public bool Manual;
    }
    class ULeft
    {
        public string Kind = "", Target = "", Extra = "", Why = "";
        public long Size = -1;
        public bool Strong;
    }

    class UninstallPage : Panel
    {
        static readonly string[] Generic = { "windows", "microsoft", "system", "common", "program", "programs", "update", "updates", "software", "data", "temp", "cache", "setup", "install", "installer", "drivers", "driver", "intel", "amd", "nvidia", "google", "apps", "app", "files", "microsoftedge", "internet", "explorer", "defender", "visual", "runtime" };
        ListView lvP = new XpList(), lvL = new XpList();
        XpText txFind; Label lblInfo, lblMsg, lblSel; XpProgress pb;
        XpOption chkBin, chkReg, chkRestore;
        XpButton bUn, bForce, bScan, bDel, bKill;
        LTip tipU = new LTip();
        UProg lastProg; static int rebootN;
        List<UProg> progs = new List<UProg>();
        UProg sel; bool busy; int sortCol; bool sortAsc = true;

        public UninstallPage()
        {
            Dock = DockStyle.Fill;
            XpGroup g1 = new XpGroup(); g1.Title = "1. Escolha o programa que quer remover"; g1.SetBounds(0, 0, 676, 262);
            UI.Lbl(g1, "Buscar:", 14, 30, Xp.UiBold);
            txFind = UI.Txt(g1, 66, 26, 200, "");
            txFind.Box.TextChanged += delegate { FillProgs(); };
            UI.Btn(g1, "Atualizar lista", 276, 25, 100, delegate { LoadProgs(); });
            UI.Btn(g1, "Escolher pasta ou .exe...", 382, 25, 160, delegate { PickManual(); });
            lblSel = UI.Lbl(g1, "", 548, 30, Xp.Ui, Xp.Dim);
            lvP.SetBounds(12, 56, 652, 156); lvP.View = View.Details; lvP.FullRowSelect = true; lvP.GridLines = true; lvP.HideSelection = false; lvP.MultiSelect = false;
            lvP.BackColor = Xp.EditBg; lvP.Font = Xp.Ui;
            lvP.Columns.Add("Programa", 250); lvP.Columns.Add("Editora", 130); lvP.Columns.Add("Versão", 80); lvP.Columns.Add("Tamanho", 70); lvP.Columns.Add("Instalado", 80);
            UI.DoubleBuffer(lvP);
            lvP.ColumnClick += delegate(object o, ColumnClickEventArgs e) { if (sortCol == e.Column) sortAsc = !sortAsc; else { sortCol = e.Column; sortAsc = true; } FillProgs(); };
            lvP.SelectedIndexChanged += delegate
            {
                sel = lvP.SelectedItems.Count > 0 ? lvP.SelectedItems[0].Tag as UProg : null;
                lblSel.Text = sel == null ? "" : "Selecionado";
                UpdateButtons();
            };
            lvP.DoubleClick += delegate { if (sel != null) DoUninstall(false); };
            g1.Controls.Add(lvP);
            bUn = UI.Btn(g1, "Desinstalar e limpar restos", 12, 222, 178, delegate { DoUninstall(false); });
            bForce = UI.Btn(g1, "Remoção forçada", 194, 222, 112, delegate { DoUninstall(true); });
            bScan = UI.Btn(g1, "So procurar restos", 310, 222, 112, delegate { StartScan(sel, "Procurando restos..."); });
            bKill = UI.Btn(g1, "Encerrar processos", 426, 222, 118, delegate { KillNow(); });
            bUn.Height = bForce.Height = bScan.Height = bKill.Height = 28;
            tipU.SetToolTip(bKill, "Finaliza todos os programas abertos que rodam da pasta do programa selecionado (e para os serviços dele). Use antes de desinstalar se ele estiver aberto.");
            lblInfo = UI.Lbl(g1, "Duplo clique desinstala.", 550, 228, Xp.Ui, Xp.Dim);
            lblInfo.AutoSize = false; lblInfo.SetBounds(550, 218, 114, 40);
            Controls.Add(g1);

            XpGroup g2 = new XpGroup(); g2.Title = "2. Restos encontrados (revise e remova)"; g2.SetBounds(0, 270, 676, 258);
            lvL.SetBounds(12, 26, 652, 148); lvL.View = View.Details; lvL.CheckBoxes = true; lvL.FullRowSelect = true; lvL.GridLines = true; lvL.HideSelection = false;
            lvL.BackColor = Xp.EditBg; lvL.Font = Xp.Ui;
            lvL.Columns.Add("Tipo", 90); lvL.Columns.Add("Local", 380); lvL.Columns.Add("Tamanho", 70); lvL.Columns.Add("Certeza", 60);
            UI.DoubleBuffer(lvL);
            g2.Controls.Add(lvL);
            chkBin = UI.Opt(g2, "Pastas e arquivos vão para a Lixeira (podem ser recuperados)", 12, 182, 400, false); chkBin.Checked = true;
            chkReg = UI.Opt(g2, "Salvar cópia (.reg) das chaves do registro antes de apagar", 12, 202, 400, false); chkReg.Checked = true;
            chkRestore = UI.Opt(g2, "Criar ponto de restauração do Windows antes", 12, 222, 400, false);
            UI.Btn(g2, "Marcar tudo", 420, 180, 110, delegate { foreach (ListViewItem it in lvL.Items) it.Checked = true; });
            UI.Btn(g2, "Desmarcar tudo", 536, 180, 128, delegate { foreach (ListViewItem it in lvL.Items) it.Checked = false; });
            bDel = UI.Btn(g2, "Remover itens marcados", 420, 214, 244, delegate { DoRemove(); });
            bDel.Height = 30;
            Controls.Add(g2);

            pb = new XpProgress(); pb.SetBounds(0, 534, 676, 14); Controls.Add(pb);
            lblMsg = UI.Lbl(this, "Dica: itens com 'Certeza: alta' são ligados ao programa; os de certeza 'media' vêm desmarcados - confira antes.", 0, 552, Xp.Ui, Xp.Dim);
            lblMsg.AutoSize = false; lblMsg.SetBounds(0, 550, 676, 34);
            UpdateButtons();
            Timer tk = new Timer(); tk.Interval = 100; tk.Tick += delegate { if (busy) pb.Step(); }; tk.Start();
            VisibleChanged += delegate { if (Visible && progs.Count == 0 && !busy) LoadProgs(); };
        }

        void UpdateButtons()
        {
            bool ok = sel != null && !busy;
            bUn.Enabled = ok && !sel.Manual && sel.Uninst.Length > 0;
            bForce.Enabled = ok; bScan.Enabled = ok; bKill.Enabled = ok; bDel.Enabled = !busy;
        }
        void Msg(string t) { lblMsg.Text = t; }
        void Busy(bool b, string t)
        {
            busy = b; pb.Marquee = b; pb.Value = 0; if (t != null) Msg(t);
            UpdateButtons();
        }

        // ----- lista de programas instalados -----
        void LoadProgs()
        {
            Busy(true, "Lendo a lista de programas instalados...");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<UProg> l = new List<UProg>();
                try
                {
                    ReadUninstall(Registry.LocalMachine, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", l);
                    ReadUninstall(Registry.LocalMachine, "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", l);
                    ReadUninstall(Registry.CurrentUser, "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", l);
                }
                catch (Exception) { }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        List<UProg> manual = progs.FindAll(delegate(UProg x) { return x.Manual; });
                        progs = l; progs.AddRange(manual);
                        FillProgs(); Busy(false, "Pronto: " + l.Count + " programas. Escolha um na lista.");
                    }));
                }
                catch (Exception) { }
            });
        }
        static string V(RegistryKey k, string n) { try { object o = k.GetValue(n); return o == null ? "" : o.ToString().Trim(); } catch (Exception) { return ""; } }
        static void ReadUninstall(RegistryKey root, string hive, string path, List<UProg> into)
        {
            using (RegistryKey rk = root.OpenSubKey(path))
            {
                if (rk == null) return;
                foreach (string sn in rk.GetSubKeyNames())
                {
                    try
                    {
                        using (RegistryKey k = rk.OpenSubKey(sn))
                        {
                            if (k == null) continue;
                            string name = V(k, "DisplayName");
                            if (name.Length == 0 || V(k, "SystemComponent") == "1" || V(k, "ParentKeyName").Length > 0) continue;
                            if (V(k, "ReleaseType") == "Update" || V(k, "ReleaseType") == "Hotfix") continue;
                            UProg p = new UProg();
                            p.Name = name; p.Publisher = V(k, "Publisher"); p.Version = V(k, "DisplayVersion");
                            p.Loc = V(k, "InstallLocation").Trim('"').TrimEnd('\\');
                            p.Uninst = V(k, "UninstallString"); p.KeyFull = hive + "\\" + path + "\\" + sn;
                            long kb; if (long.TryParse(V(k, "EstimatedSize"), out kb)) p.SizeKb = kb;
                            string d = V(k, "InstallDate");
                            if (d.Length == 8) p.Date = d.Substring(6, 2) + "/" + d.Substring(4, 2) + "/" + d.Substring(0, 4);
                            if (p.Loc.Length == 0)
                            {
                                string ico = V(k, "DisplayIcon").Trim('"');
                                int c = ico.IndexOf(','); if (c > 0) ico = ico.Substring(0, c);
                                try { if (ico.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(ico)) p.Loc = Path.GetDirectoryName(ico); } catch (Exception) { }
                            }
                            into.Add(p);
                        }
                    }
                    catch (Exception) { }
                }
            }
        }
        void FillProgs()
        {
            string f = txFind.Box.Text.Trim().ToLower();
            List<UProg> l = progs.FindAll(delegate(UProg x) { return f.Length == 0 || x.Name.ToLower().Contains(f) || x.Publisher.ToLower().Contains(f); });
            int sc = sortCol; bool asc = sortAsc;
            l.Sort(delegate(UProg a, UProg b)
            {
                int r;
                if (sc == 3) r = a.SizeKb.CompareTo(b.SizeKb);
                else if (sc == 4) r = DateKey(a.Date).CompareTo(DateKey(b.Date));
                else
                {
                    string x = sc == 1 ? a.Publisher : sc == 2 ? a.Version : a.Name, y = sc == 1 ? b.Publisher : sc == 2 ? b.Version : b.Name;
                    r = string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
                }
                return asc ? r : -r;
            });
            string keep = sel != null ? sel.Name : null;
            lvP.BeginUpdate(); lvP.Items.Clear();
            foreach (UProg p in l)
            {
                ListViewItem it = new ListViewItem(p.Name);
                it.SubItems.Add(p.Publisher); it.SubItems.Add(p.Version);
                it.SubItems.Add(p.SizeKb > 0 ? (p.SizeKb >= 1024 ? (p.SizeKb / 1024.0).ToString("0.#") + " MB" : p.SizeKb + " KB") : "");
                it.SubItems.Add(p.Date);
                it.Tag = p;
                if (p.Manual) it.ForeColor = Xp.Good;
                lvP.Items.Add(it);
                if (keep != null && p.Name == keep) { it.Selected = true; }
            }
            lvP.EndUpdate();
        }
        static string DateKey(string d) { return d.Length == 10 ? d.Substring(6, 4) + d.Substring(3, 2) + d.Substring(0, 2) : ""; }

        void PickManual()
        {
            string path = null;
            int r = AskPick();
            if (r == 0) return;
            if (r == 1)
            {
                using (FolderBrowserDialog fd = new FolderBrowserDialog())
                {
                    fd.Description = Lang.T("Escolha a pasta do programa");
                    if (fd.ShowDialog(this) != DialogResult.OK) return;
                    path = fd.SelectedPath;
                }
            }
            else
            {
                using (OpenFileDialog od = new OpenFileDialog())
                {
                    od.Filter = Lang.T("Programas (*.exe)|*.exe|Todos|*.*");
                    if (od.ShowDialog(this) != DialogResult.OK) return;
                    path = od.FileName;
                }
            }
            UProg p = new UProg(); p.Manual = true;
            string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            string nm = Directory.Exists(path) ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path);
            try { if (File.Exists(path)) { FileVersionInfo vi = FileVersionInfo.GetVersionInfo(path); if (!string.IsNullOrEmpty(vi.ProductName)) nm = vi.ProductName.Trim(); if (!string.IsNullOrEmpty(vi.CompanyName)) p.Publisher = vi.CompanyName.Trim(); } } catch (Exception) { }
            p.Name = nm; p.Loc = folder.TrimEnd('\\'); p.Version = "(manual)";
            if (!SafeFolder(p.Loc)) { Dlg.Show("Essa pasta e protegida pelo sistema e não pode ser usada aqui:\n" + p.Loc, "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            progs.Insert(0, p);
            sortCol = -1; txFind.Box.Text = ""; FillProgs();
            if (lvP.Items.Count > 0) { sel = null; foreach (ListViewItem it in lvP.Items) if (it.Tag == p) { it.Selected = true; it.EnsureVisible(); break; } }
            Msg("Adicionado: " + p.Name + ". Use 'Remoção forçada' ou 'So procurar restos'.");
        }

        // pergunta com botoes Pasta / Exe / Cancelar: 1 = pasta, 2 = exe, 0 = cancelar
        int AskPick()
        {
            int res = 0;
            using (Form f = new Form())
            {
                f.Text = "BunoTunner - Adicionar manualmente"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false; f.ClientSize = new Size(380, 120); f.BackColor = Xp.Face; f.Font = Xp.Ui;
                Label l = new Label(); l.Text = "Use isto para programas que não aparecem na lista (portáteis, restos de algo já removido, etc.).\n\nO que você quer escolher?";
                l.SetBounds(14, 12, 352, 56); l.ForeColor = Xp.Text; f.Controls.Add(l);
                XpButton bp = new XpButton(); bp.Text = "Pasta"; bp.SetBounds(30, 80, 100, 28); bp.Click += delegate { res = 1; f.Close(); }; f.Controls.Add(bp);
                XpButton be = new XpButton(); be.Text = "Exe"; be.SetBounds(140, 80, 100, 28); be.Click += delegate { res = 2; f.Close(); }; f.Controls.Add(be);
                XpButton bc = new XpButton(); bc.Text = "Cancelar"; bc.SetBounds(250, 80, 100, 28); bc.Click += delegate { res = 0; f.Close(); }; f.Controls.Add(bc);
                f.CancelButton = null;
                Lang.Apply(f, null);
                f.ShowDialog(this);
            }
            return res;
        }

        // ----- nomes e seguranca -----
        static string Norm(string s) { return Regex.Replace((s ?? "").ToLower(), "[^a-z0-9]", ""); }
        static string Core(string name)
        {
            string s = Regex.Replace(name ?? "", @"\([^)]*\)|\[[^\]]*\]", " ");
            s = Regex.Replace(s, @"\b[vV]?\d+([.\-_]\d+)*\b", " ");
            s = Regex.Replace(s, @"\b(x64|x86|64-bit|32-bit|64bit|32bit|edition|version|versao)\b", " ", RegexOptions.IgnoreCase);
            return Regex.Replace(s, @"\s+", " ").Trim();
        }
        static bool WeakName(string nc)
        {
            if (nc.Length < 4) return true;
            foreach (string g in Generic) if (nc == g) return true;
            return false;
        }
        static string[] ProtectedDirs()
        {
            List<string> l = new List<string>();
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            l.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
            l.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
            return l.ToArray();
        }
        // pasta valida para apagar: nao e raiz de disco, nem pasta do sistema, nem uma das pastas-mae protegidas
        static bool SafeFolder(string p)
        {
            try
            {
                if (string.IsNullOrEmpty(p)) return false;
                string full = Path.GetFullPath(p).TrimEnd('\\');
                if (full.Length < 4 || Path.GetPathRoot(full).TrimEnd('\\').Equals(full, StringComparison.OrdinalIgnoreCase)) return false;
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (full.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return false;
                foreach (string d in ProtectedDirs())
                    if (!string.IsNullOrEmpty(d) && full.Equals(d.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
                string[] parts = full.Split('\\');
                return parts.Length >= 3;
            }
            catch (Exception) { return false; }
        }
        static bool SafeFile(string p)
        {
            try
            {
                string full = Path.GetFullPath(p);
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                return !full.StartsWith(win, StringComparison.OrdinalIgnoreCase) && Path.GetPathRoot(full).TrimEnd('\\').Length < full.TrimEnd('\\').Length;
            }
            catch (Exception) { return false; }
        }

        // ----- desinstalar -----
        void DoUninstall(bool force)
        {
            if (sel == null || busy) return;
            UProg p = sel;
            if (!force)
            {
                if (p.Uninst.Length == 0) { Msg("Este item não tem desinstalador. Use 'Remoção forçada'."); return; }
                if (Dlg.Show("Desinstalar \"" + p.Name + "\"?\n\n1) O desinstalador oficial será aberto (siga as telas dele).\n2) Depois o BunoTunner procura o que sobrou para você revisar.", "BunoTunner - Desinstalar", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                if (chkRestore.Checked) MakeRestorePoint(p.Name);
                Busy(true, "Desinstalador aberto: conclua as telas dele... (a busca de restos começa quando ele terminar)");
                string cmd = p.Uninst;
                if (Regex.IsMatch(cmd, @"msiexec", RegexOptions.IgnoreCase)) cmd = Regex.Replace(cmd, @"/I(\s*\{)", "/X$1", RegexOptions.IgnoreCase);
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string err = null;
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"");
                        psi.UseShellExecute = false; psi.CreateNoWindow = true;
                        using (Process pr = Process.Start(psi)) pr.WaitForExit();
                    }
                    catch (Exception ex) { err = ex.Message; }
                    try { BeginInvoke(new Action(delegate { Busy(false, err == null ? "Desinstalador terminou." : "Falha ao abrir o desinstalador: " + err); StartScan(p, "Procurando restos de " + p.Name + "..."); })); } catch (Exception) { }
                });
            }
            else
            {
                if (Dlg.Show("REMOÇÃO FORÇADA de \"" + p.Name + "\"\n\nIsto NÃO usa o desinstalador: encerra os processos do programa, para os serviços dele e lista tudo o que for dele (pasta, registro, inicialização, tarefas) para você apagar.\n\nNada e apagado antes de você revisar e confirmar a lista. Continuar?", "BunoTunner - Remoção forçada", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                if (chkRestore.Checked) MakeRestorePoint(p.Name);
                int k = KillProcs(p);
                StartScan(p, k > 0 ? k + " processo(s) encerrado(s). Procurando restos..." : "Procurando restos...");
            }
        }
        void KillNow()
        {
            if (sel == null) return;
            if (string.IsNullOrEmpty(sel.Loc) || !SafeFolder(sel.Loc)) { Msg("Não sei qual é a pasta deste programa; use 'Escolher pasta ou .exe' para informar."); return; }
            if (Dlg.Show("Finalizar todos os processos de \"" + sel.Name + "\" (pasta " + sel.Loc + ")?\nTrabalho não salvo nele será perdido.", "BunoTunner", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            int n = KillProcs(sel);
            Msg(n > 0 ? n + " processo(s) finalizado(s)." : "Nenhum processo desse programa esta aberto.");
        }
        static int KillProcs(UProg p)
        {
            int n = 0;
            if (string.IsNullOrEmpty(p.Loc) || !SafeFolder(p.Loc)) return 0;
            string root = p.Loc.TrimEnd('\\') + "\\";
            try
            {
                using (RegistryKey sk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                    if (sk != null)
                        foreach (string sn in sk.GetSubKeyNames())
                            using (RegistryKey k = sk.OpenSubKey(sn))
                                if (k != null && V(k, "ImagePath").IndexOf(p.Loc, StringComparison.OrdinalIgnoreCase) >= 0) { int c; Proc.Run("sc.exe", "stop \"" + sn + "\"", 10000, out c); }
            }
            catch (Exception) { }
            foreach (Process pr in Process.GetProcesses())
            {
                try
                {
                    string f = pr.MainModule.FileName;
                    if (f.StartsWith(root, StringComparison.OrdinalIgnoreCase) && pr.Id != Process.GetCurrentProcess().Id) { pr.Kill(); pr.WaitForExit(2000); n++; }
                }
                catch (Exception) { }
                finally { pr.Dispose(); }
            }
            return n;
        }
        void MakeRestorePoint(string name)
        {
            Busy(true, "Criando ponto de restauração (pode levar um minuto)...");
            Update();
            int code;
            string o = Proc.Run("powershell.exe", "-NoProfile -Command \"Checkpoint-Computer -Description 'Buno Tunner - antes de remover " + name.Replace("'", "").Replace("\"", "") + "' -RestorePointType MODIFY_SETTINGS\"", 120000, out code);
            Busy(false, code == 0 ? "Ponto de restauração criado." : "Não foi possível criar o ponto de restauração (o Windows limita 1 a cada 24 h, ou a proteção do sistema está desligada). Continuando.");
        }

        // ----- busca de restos -----
        void StartScan(UProg p, string msg)
        {
            if (p == null || busy) return;
            Busy(true, msg);
            lastProg = p;
            lvL.Items.Clear();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<ULeft> res = new List<ULeft>();
                string err = null;
                try { res = Scan(p); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        lvL.BeginUpdate();
                        foreach (ULeft l in res)
                        {
                            ListViewItem it = new ListViewItem(l.Kind);
                            it.SubItems.Add(l.Target + (l.Extra.Length > 0 ? "   [" + l.Extra + "]" : ""));
                            it.SubItems.Add(l.Size >= 0 ? Fmt(l.Size) : "");
                            it.SubItems.Add(l.Strong ? "alta" : "media");
                            it.Tag = l; it.Checked = l.Strong;
                            if (!l.Strong) it.ForeColor = Xp.Dim;
                            lvL.Items.Add(it);
                        }
                        lvL.EndUpdate();
                        Busy(false, err != null ? "Erro na busca: " + err : res.Count == 0 ? "Nada sobrou: nenhum resto encontrado para " + p.Name + "." : res.Count + " item(ns) encontrado(s). Revise a lista (só os de certeza alta vêm marcados) e clique em 'Remover itens marcados'.");
                    }));
                }
                catch (Exception) { }
            });
        }
        static string Fmt(long b) { return b >= 1073741824 ? (b / 1073741824.0).ToString("0.0") + " GB" : b >= 1048576 ? (b / 1048576.0).ToString("0.0") + " MB" : (b / 1024) + " KB"; }
        static long DirSize(string d, ref int budget)
        {
            long t = 0;
            try
            {
                foreach (string f in Directory.GetFiles(d)) { if (--budget < 0) return t; try { t += new FileInfo(f).Length; } catch (Exception) { } }
                foreach (string s in Directory.GetDirectories(d)) { if (budget < 0) return t; t += DirSize(s, ref budget); }
            }
            catch (Exception) { }
            return t;
        }

        static bool NameHit(string fname, string nc, string np, out bool strong)
        {
            strong = false;
            string n = Norm(fname);
            if (n.Length == 0 || WeakName(nc)) return false;
            if (n == nc) { strong = true; return true; }
            if (nc.Length >= 6 && (n.StartsWith(nc) || nc.StartsWith(n) && n.Length >= 6)) return true;
            return false;
        }

        static List<ULeft> Scan(UProg p)
        {
            List<ULeft> res = new List<ULeft>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string core = Core(p.Name), nc = Norm(core), np = Norm(p.Publisher);
            string loc = (p.Loc ?? "").TrimEnd('\\');
            bool locOk = loc.Length > 0 && SafeFolder(loc);

            // pasta de instalacao
            if (locOk && Directory.Exists(loc))
            {
                int bud = 40000;
                ULeft l = new ULeft(); l.Kind = "Pasta"; l.Target = loc; l.Strong = true; l.Why = "pasta de instalação"; l.Size = DirSize(loc, ref bud);
                res.Add(l); seen.Add(loc);
            }
            // pastas/arquivos com o nome do programa
            List<string> roots = new List<string>();
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                ScanDir(root, nc, np, res, seen, 0);
            }
            // registro
            ScanReg(p, nc, np, loc, locOk, res, seen);
            // servicos
            try
            {
                using (RegistryKey sk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                    if (sk != null)
                        foreach (string sn in sk.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey k = sk.OpenSubKey(sn))
                                {
                                    string img = V(k, "ImagePath"), dn = V(k, "DisplayName");
                                    bool byPath = locOk && img.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
                                    bool st; bool byName = NameHit(sn, nc, np, out st) || NameHit(dn, nc, np, out st);
                                    if (!byPath && !byName) continue;
                                    ULeft l = new ULeft(); l.Kind = "Serviço"; l.Target = sn; l.Extra = img.Length > 60 ? img.Substring(0, 60) + "..." : img; l.Strong = byPath;
                                    res.Add(l);
                                }
                            }
                            catch (Exception) { }
                        }
            }
            catch (Exception) { }
            // tarefas agendadas
            try
            {
                string td = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\Tasks");
                if (Directory.Exists(td))
                    foreach (string f in Directory.GetFiles(td, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            string rel = f.Substring(td.Length);
                            bool st; bool byName = NameHit(Path.GetFileName(f), nc, np, out st);
                            bool byPath = false;
                            if (!byName && locOk && new FileInfo(f).Length < 200000) byPath = File.ReadAllText(f).IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!byName && !byPath) continue;
                            if (rel.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase)) continue;
                            ULeft l = new ULeft(); l.Kind = "Tarefa"; l.Target = rel; l.Strong = byPath || st;
                            res.Add(l);
                        }
                        catch (Exception) { }
                    }
            }
            catch (Exception) { }
            return res;
        }
        static void ScanDir(string dir, string nc, string np, List<ULeft> res, HashSet<string> seen, int depth)
        {
            string[] ents;
            try { ents = Directory.GetFileSystemEntries(dir); } catch (Exception) { return; }
            foreach (string e in ents)
            {
                string name = Path.GetFileName(e);
                bool isDir = Directory.Exists(e);
                string baseName = isDir ? name : Path.GetFileNameWithoutExtension(name);
                bool strong;
                if (NameHit(baseName, nc, np, out strong) && !seen.Contains(e) && (isDir ? SafeFolder(e) : SafeFile(e)))
                {
                    ULeft l = new ULeft(); l.Kind = isDir ? "Pasta" : (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? "Atalho" : "Arquivo"); l.Target = e; l.Strong = strong;
                    if (isDir) { int bud = 30000; l.Size = DirSize(e, ref bud); } else { try { l.Size = new FileInfo(e).Length; } catch (Exception) { } }
                    res.Add(l); seen.Add(e);
                }
                else if (isDir && depth == 0 && np.Length >= 4 && Norm(name) == np)
                {
                    // pasta da editora (ex.: ...\Editora\Programa): olha um nivel dentro dela
                    ScanDir(e, nc, np, res, seen, 1);
                }
            }
        }
        static void ScanReg(UProg p, string nc, string np, string loc, bool locOk, List<ULeft> res, HashSet<string> seen)
        {
            if (p.KeyFull.Length > 0 && seen.Add(p.KeyFull))
            {
                ULeft u = new ULeft(); u.Kind = "Registro"; u.Target = p.KeyFull; u.Strong = true; u.Extra = "entrada em Programas e Recursos"; res.Add(u);
            }
            string[] sw = { @"SOFTWARE", @"SOFTWARE\WOW6432Node" };
            RegistryKey[] hv = { Registry.CurrentUser, Registry.LocalMachine };
            string[] hn = { "HKCU", "HKLM" };
            for (int h = 0; h < 2; h++)
                foreach (string b in sw)
                {
                    try
                    {
                        using (RegistryKey k = hv[h].OpenSubKey(b))
                        {
                            if (k == null) continue;
                            foreach (string sn in k.GetSubKeyNames())
                            {
                                bool st;
                                if (NameHit(sn, nc, np, out st))
                                {
                                    string full = hn[h] + "\\" + b + "\\" + sn;
                                    if (seen.Add(full)) { ULeft l = new ULeft(); l.Kind = "Registro"; l.Target = full; l.Strong = st; res.Add(l); }
                                }
                                else if (np.Length >= 4 && Norm(sn) == np)
                                {
                                    using (RegistryKey pk = k.OpenSubKey(sn))
                                        if (pk != null)
                                            foreach (string cn in pk.GetSubKeyNames())
                                            {
                                                bool st2;
                                                if (!NameHit(cn, nc, np, out st2)) continue;
                                                string full = hn[h] + "\\" + b + "\\" + sn + "\\" + cn;
                                                if (seen.Add(full)) { ULeft l = new ULeft(); l.Kind = "Registro"; l.Target = full; l.Strong = st2; res.Add(l); }
                                            }
                                }
                            }
                        }
                    }
                    catch (Exception) { }
                }
            // programas que iniciam com o Windows
            string[] runs = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" };
            for (int h = 0; h < 2; h++)
                foreach (string rp in runs)
                {
                    try
                    {
                        using (RegistryKey k = hv[h].OpenSubKey(rp))
                        {
                            if (k == null) continue;
                            foreach (string vn in k.GetValueNames())
                            {
                                string data = V(k, vn);
                                bool byPath = locOk && data.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
                                bool st; bool byName = NameHit(vn, nc, np, out st);
                                if (!byPath && !byName) continue;
                                ULeft l = new ULeft(); l.Kind = "Inicialização"; l.Target = hn[h] + "\\" + rp; l.Extra = vn; l.Strong = byPath || st; res.Add(l);
                            }
                        }
                    }
                    catch (Exception) { }
                }
        }

        // ----- remocao -----
        void DoRemove()
        {
            List<ULeft> items = new List<ULeft>();
            foreach (ListViewItem it in lvL.CheckedItems) items.Add((ULeft)it.Tag);
            if (items.Count == 0) { Msg("Nenhum item marcado."); return; }
            bool bin = chkBin.Checked, bak = chkReg.Checked;
            if (Dlg.Show("Remover " + items.Count + " item(ns) marcado(s)?\n\n" + (bin ? "Pastas e arquivos irão para a Lixeira.\n" : "Pastas e arquivos serão APAGADOS de vez.\n") + (bak ? "Uma cópia das chaves do registro será salva em BunoTunner_backup." : "Sem cópia do registro."), "BunoTunner - Remover", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            Busy(true, "Removendo...");
            UProg kp = lastProg;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0; rebootN = 0; List<string> fails = new List<string>();
                if (kp != null) KillProcs(kp);
                string bdir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BunoTunner_backup\\" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                int regN = 0;
                foreach (ULeft l in items)
                {
                    try
                    {
                        string err = RemoveOne(l, bin, bak, bdir, ref regN);
                        if (err == null) ok++; else fails.Add(l.Kind + " " + l.Target + " -> " + err);
                    }
                    catch (Exception ex) { fails.Add(l.Kind + " " + l.Target + " -> " + ex.Message); }
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        Busy(false, ok + " item(ns) removido(s)" + (fails.Count > 0 ? ", " + fails.Count + " com falha." : ".") + (rebootN > 0 ? " " + rebootN + " arquivo(s) em uso serão apagados no próximo reinicio do Windows." : "") + (bak && regN > 0 ? " Copias do registro em BunoTunner_backup." : ""));
                        if (fails.Count > 0)
                            Dlg.Show("Não foi possível remover:\n\n" + string.Join("\n", fails.GetRange(0, Math.Min(12, fails.Count)).ToArray()) + "\n\nSe o arquivo está em uso, feche o programa (ou use 'Remoção forçada') e tente de novo; ou reinicie o PC.", "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        LoadProgs();
                        lvL.Items.Clear();
                    }));
                }
                catch (Exception) { }
            });
        }
        static string RemoveOne(ULeft l, bool bin, bool bak, string bdir, ref int regN)
        {
            switch (l.Kind)
            {
                case "Pasta":
                    if (!Directory.Exists(l.Target)) return null;
                    if (!SafeFolder(l.Target)) return "pasta protegida";
                    try
                    {
                        if (bin) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(l.Target, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        else Directory.Delete(l.Target, true);
                        if (!Directory.Exists(l.Target)) return null;
                    }
                    catch (Exception) { }
                    return ForceDelete(l.Target, true);
                case "Arquivo":
                case "Atalho":
                    if (!File.Exists(l.Target)) return null;
                    if (!SafeFile(l.Target)) return "arquivo protegido";
                    try
                    {
                        if (bin) Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(l.Target, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        else File.Delete(l.Target);
                        if (!File.Exists(l.Target)) return null;
                    }
                    catch (Exception) { }
                    return ForceDelete(l.Target, false);
                case "Registro":
                    {
                        if (bak) { Directory.CreateDirectory(bdir); int c; Proc.Run("reg.exe", "export \"" + l.Target + "\" \"" + Path.Combine(bdir, "chave_" + (++regN) + ".reg") + "\" /y", 20000, out c); }
                        string sub; RegistryKey root = RegRoot(l.Target, out sub);
                        if (root == null || sub.Length < 12 || sub.IndexOf('\\') < 0) return "chave protegida";
                        try { root.DeleteSubKeyTree(sub, false); return null; }
                        catch (Exception)
                        {
                            int c; string o = Proc.Run("reg.exe", "delete \"" + l.Target + "\" /f", 15000, out c);
                            return c == 0 ? null : "sem permissão no registro: " + o;
                        }
                    }
                case "Inicialização":
                    {
                        string sub; RegistryKey root = RegRoot(l.Target, out sub);
                        if (root == null) return "chave inválida";
                        if (bak) { Directory.CreateDirectory(bdir); File.AppendAllText(Path.Combine(bdir, "inicializacao.txt"), l.Target + "  " + l.Extra + " = " + ReadVal(root, sub, l.Extra) + "\r\n"); }
                        using (RegistryKey k = root.OpenSubKey(sub, true)) if (k != null) k.DeleteValue(l.Extra, false);
                        return null;
                    }
                case "Serviço":
                    {
                        int c;
                        Proc.Run("sc.exe", "stop \"" + l.Target + "\"", 15000, out c);
                        string o = Proc.Run("sc.exe", "delete \"" + l.Target + "\"", 15000, out c);
                        return c == 0 ? null : o;
                    }
                case "Tarefa":
                    {
                        int c;
                        string o = Proc.Run("schtasks.exe", "/delete /tn \"" + l.Target + "\" /f", 15000, out c);
                        return c == 0 ? null : o;
                    }
            }
            return "tipo desconhecido";
        }
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool MoveFileEx(string from, string to, int flags);

        // apagar a forca: toma posse, da controle total ao Administradores, tira somente-leitura e tenta de novo;
        // o que continuar em uso e agendado para apagar no proximo reinicio do Windows
        static string ForceDelete(string path, bool dir)
        {
            int c;
            Proc.Run("takeown.exe", "/f \"" + path + "\"" + (dir ? " /r" : ""), 60000, out c);
            Proc.Run("icacls.exe", "\"" + path + "\" /grant *S-1-5-32-544:F" + (dir ? " /t" : "") + " /c /q", 60000, out c);
            try
            {
                if (dir)
                {
                    foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) { try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch (Exception) { } }
                    try { Directory.Delete(path, true); } catch (Exception) { }
                }
                else { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
            }
            catch (Exception) { }
            if (dir ? !Directory.Exists(path) : !File.Exists(path)) return null;
            // ainda em uso: agenda para o reinicio (arquivos primeiro, pastas da mais funda para a mais rasa)
            try
            {
                List<string> files = new List<string>(), dirs = new List<string>();
                if (dir) { files.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories)); dirs.AddRange(Directory.GetDirectories(path, "*", SearchOption.AllDirectories)); dirs.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); }); dirs.Add(path); }
                else files.Add(path);
                bool ok = true;
                foreach (string f in files) { if (MoveFileEx(f, null, 4)) rebootN++; else ok = false; }
                foreach (string d in dirs) if (!MoveFileEx(d, null, 4)) ok = false;
                return ok ? null : "em uso e não foi possível agendar a exclusão";
            }
            catch (Exception ex) { return ex.Message; }
        }

        static string ReadVal(RegistryKey root, string sub, string name)
        {
            try { using (RegistryKey k = root.OpenSubKey(sub)) return k == null ? "" : V(k, name); } catch (Exception) { return ""; }
        }
        static RegistryKey RegRoot(string full, out string sub)
        {
            sub = "";
            int i = full.IndexOf('\\'); if (i < 0) return null;
            string h = full.Substring(0, i); sub = full.Substring(i + 1);
            if (h == "HKLM") return Registry.LocalMachine;
            if (h == "HKCU") return Registry.CurrentUser;
            return null;
        }
    }

    // ------------------------------------------------------------------
    //  Internet: teste de velocidade real (ping, download, upload) + informacoes da rede (so ao clicar)
    // ------------------------------------------------------------------
    class SpeedGauge : Control
    {
        public double Value;        // alvo (Mbps)
        public string Caption = "", Unit = "Mbps";
        double shown, max = 100;
        static readonly double[] Scales = { 10, 25, 50, 100, 250, 500, 1000, 2500, 5000 };
        Timer tm = new Timer();
        public SpeedGauge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            tm.Interval = 40;
            tm.Tick += delegate
            {
                if (!Visible) return;
                for (int i = 0; i < Scales.Length - 1 && Value * 1.08 > max && max < Scales[Scales.Length - 1]; i++)
                    foreach (double sc in Scales) if (sc > max) { max = sc; break; }
                double d = Value - shown;
                if (Math.Abs(d) > 0.01) { shown += d * 0.22; Invalidate(); }
            };
            tm.Start();
        }
        public void Reset() { Value = 0; shown = 0; max = 100; Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) tm.Dispose(); base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            float size = Math.Min(Width, Height * 1.15f) - 24;
            if (size < 60) return;
            float cx = Width / 2f, cy = Height * 0.56f, rad = size / 2f;
            RectangleF box = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
            float start = 135f, sweep = 270f;
            using (Pen track = new Pen(Color.FromArgb(70, Xp.Dim), rad * 0.13f)) { track.StartCap = LineCap.Round; track.EndCap = LineCap.Round; g.DrawArc(track, box, start, sweep); }
            double frac = Math.Max(0, Math.Min(1, shown / max));
            Color col = frac < 0.55 ? Color.FromArgb(40, 190, 80) : frac < 0.8 ? Color.FromArgb(235, 190, 30) : Color.FromArgb(235, 90, 40);
            if (frac > 0.002)
                using (Pen vp = new Pen(col, rad * 0.13f)) { vp.StartCap = LineCap.Round; vp.EndCap = LineCap.Round; g.DrawArc(vp, box, start, (float)(sweep * frac)); }
            using (Pen tk = new Pen(Xp.Text, 1.5f))
            using (Font f = new Font(Xp.Ui.FontFamily, 8f))
                for (int i = 0; i <= 10; i++)
                {
                    double a = (start + sweep * i / 10.0) * Math.PI / 180;
                    float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                    g.DrawLine(tk, cx + ca * rad * 0.80f, cy + sa * rad * 0.80f, cx + ca * rad * 0.70f, cy + sa * rad * 0.70f);
                    if (i % 2 == 0)
                    {
                        string t = (max * i / 10.0).ToString("0");
                        Size ts = TextRenderer.MeasureText(t, f);
                        TextRenderer.DrawText(g, t, f, new Point((int)(cx + ca * rad * 0.58f - ts.Width / 2f), (int)(cy + sa * rad * 0.58f - ts.Height / 2f)), Xp.Dim);
                    }
                }
            double na = (start + sweep * frac) * Math.PI / 180;
            using (Pen np = new Pen(Color.FromArgb(220, 40, 40), 3f)) { np.EndCap = LineCap.Round; g.DrawLine(np, cx, cy, cx + (float)Math.Cos(na) * rad * 0.66f, cy + (float)Math.Sin(na) * rad * 0.66f); }
            g.FillEllipse(Brushes.DimGray, cx - 7, cy - 7, 14, 14);
            using (Font big = new Font(Xp.Ui.FontFamily, Math.Max(16f, rad * 0.26f), FontStyle.Bold))
            {
                string v = shown >= 100 ? shown.ToString("0") : shown.ToString("0.0");
                Size vs = TextRenderer.MeasureText(v, big);
                TextRenderer.DrawText(g, v, big, new Point((int)(cx - vs.Width / 2f), (int)(cy + rad * 0.22f)), Xp.Text);
                Size us = TextRenderer.MeasureText(Unit, Xp.UiBold);
                TextRenderer.DrawText(g, Unit, Xp.UiBold, new Point((int)(cx - us.Width / 2f), (int)(cy + rad * 0.22f + vs.Height - 2)), Xp.Dim);
            }
            string capT = Lang.T(Caption);
            Size cs = TextRenderer.MeasureText(capT, Xp.UiBold);
            TextRenderer.DrawText(g, capT, Xp.UiBold, new Point((int)(cx - cs.Width / 2f), (int)(cy - rad * 1.0f)), Xp.GroupText);
        }
    }

    // cartao de resultado (ping, jitter, download, upload...): valor grande, titulo e faixa de cor de qualidade
    class StatCard : Control
    {
        public string Caption = "", Value = "-"; public bool Small; Color accent = Color.Empty;
        public Color Accent { get { return accent; } set { accent = value; Invalidate(); } }
        public override string Text { get { return Value; } set { Value = value ?? ""; Invalidate(); } }
        public StatCard() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : Xp.Face);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath gp = new GraphicsPath())
            {
                int d = Math.Max(2, Xp.Rad * 2 + 2);
                gp.AddArc(r.X, r.Y, d, d, 180, 90); gp.AddArc(r.Right - d, r.Y, d, d, 270, 90); gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90); gp.CloseFigure();
                using (LinearGradientBrush fb = new LinearGradientBrush(r, Xp.EditBg, Color.FromArgb(Math.Max(0, Xp.EditBg.R - 14), Math.Max(0, Xp.EditBg.G - 14), Math.Max(0, Xp.EditBg.B - 14)), 90f)) g.FillPath(fb, gp);
                Color ac = accent.IsEmpty ? Xp.Sel : accent;
                g.SetClip(gp);
                using (SolidBrush ab = new SolidBrush(ac)) g.FillRectangle(ab, 0, 0, 5, Height);
                g.ResetClip();
                using (Pen bp = new Pen(Xp.TextBorder)) g.DrawPath(bp, gp);
            }
            TextRenderer.DrawText(g, Lang.T(Caption), Xp.UiBold, new Rectangle(12, 5, Width - 16, 16), Xp.GroupText, TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            Font vf = Small ? Xp.Ui : Xp.Big;
            TextRenderer.DrawText(g, Lang.T(Value), vf, new Rectangle(12, 22, Width - 16, Height - 24), Xp.Text, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | (Small ? TextFormatFlags.Top : TextFormatFlags.VerticalCenter));
        }
    }

    class NetPage : Panel
    {
        SpeedGauge gauge = new SpeedGauge();
        StatCard lblPing, lblJit, lblDown, lblUp, lblQual; Label lblMsg, lblHint;
        ListView lvHist = new XpList(); double hPing = double.NaN, hJit = double.NaN, hDown = double.NaN, hUp = double.NaN;
        XpButton bStart, bInfo, bCopy, bHide;
        TextBox tbInfo = new TextBox();
        volatile bool running, cancel;
        long bytes;
        Timer tm = new Timer();

        public NetPage()
        {
            Size = new Size(676, 586); Dock = DockStyle.Fill;   // tamanho inicial = o da area da pagina, para os ancoramentos funcionarem
            XpGroup g1 = new XpGroup(); g1.Title = "Teste de velocidade da internet"; g1.SetBounds(0, 0, 676, 300); g1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            gauge.SetBounds(10, 24, 404, 266); gauge.Caption = "Pronto"; g1.Controls.Add(gauge);
            lblPing = Card(g1, "Ping", 426, 26, 118, 62); lblJit = Card(g1, "Jitter", 546, 26, 118, 62);
            lblDown = Card(g1, "Download", 426, 94, 118, 62); lblUp = Card(g1, "Upload", 546, 94, 118, 62);
            lblQual = Card(g1, "Avaliação da conexão", 426, 162, 238, 60); lblQual.Small = true; lblQual.Text = "Faça um teste para ver se a sua conexão serve para jogos, streaming e chamadas.";
            bStart = UI.Btn(g1, "Iniciar teste", 426, 230, 238, delegate { Toggle(); });
            bStart.Height = 32;
            lblMsg = UI.Lbl(g1, "Mede ping, download e upload de verdade (servidor da Cloudflare).", 426, 268, Xp.Ui, Xp.Dim);
            lblMsg.AutoSize = false; lblMsg.SetBounds(426, 266, 238, 30);
            Controls.Add(g1);

            XpGroup gh = new XpGroup(); gh.Title = "Histórico de testes"; gh.SetBounds(0, 306, 676, 118); gh.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            ((XpList)lvHist).AutoFit = true; lvHist.SetBounds(12, 24, 560, 84); lvHist.View = View.Details; lvHist.FullRowSelect = true; lvHist.GridLines = true; lvHist.HideSelection = false;
            lvHist.BackColor = Xp.EditBg; lvHist.Font = Xp.Ui;
            lvHist.Columns.Add("Data e hora", 150); lvHist.Columns.Add("Ping", 80); lvHist.Columns.Add("Jitter", 80); lvHist.Columns.Add("Download", 120); lvHist.Columns.Add("Upload", 120);
            UI.DoubleBuffer(lvHist); gh.Controls.Add(lvHist);
            UI.Btn(gh, "Copiar", 580, 24, 84, delegate { CopyHist(); }).Height = 26;
            UI.Btn(gh, "Limpar", 580, 56, 84, delegate { lvHist.Items.Clear(); SaveHist(); }).Height = 26;
            Controls.Add(gh);
            LoadHist();

            XpGroup g2 = new XpGroup(); g2.Title = "Informações da rede"; g2.SetBounds(0, 430, 676, 156); g2.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            bInfo = UI.Btn(g2, "Mostrar informações da rede", 12, 24, 220, delegate { ShowInfo(); });
            bInfo.Height = 26;
            bCopy = UI.Btn(g2, "Copiar", 238, 24, 80, delegate { try { if (tbInfo.Text.Length > 0) Clipboard.SetText(tbInfo.Text); } catch (Exception) { } });
            bHide = UI.Btn(g2, "Ocultar", 324, 24, 80, delegate { tbInfo.Text = ""; tbInfo.Visible = false; lblHint.Visible = true; });
            bCopy.Height = bHide.Height = 26;
            lblHint = UI.Lbl(g2, "Por privacidade, nada é mostrado até você clicar. Ao clicar aparecem dados pessoais (IPs, MAC, provedor, cidade, nome do Wi-Fi).", 12, 60, Xp.Ui, Xp.Dim);
            lblHint.AutoSize = false; lblHint.SetBounds(12, 58, 650, 36);
            tbInfo.Multiline = true; tbInfo.ReadOnly = true; tbInfo.ScrollBars = ScrollBars.Vertical; tbInfo.Font = new Font("Consolas", 9f);
            tbInfo.BackColor = Xp.EditBg; tbInfo.ForeColor = Xp.Text; tbInfo.SetBounds(12, 56, 652, 90); tbInfo.Visible = false; tbInfo.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            g2.Controls.Add(tbInfo);
            Controls.Add(g2);
            tm.Interval = 200; tm.Tick += delegate { Sample(); };
        }

        StatCard Card(Control parent, string caption, int x, int y, int w, int h)
        {
            StatCard c = new StatCard(); c.Caption = caption; c.SetBounds(x, y, w, h); parent.Controls.Add(c); return c;
        }

        static Color Q(double v, double good, double ok, bool lowerBetter)
        {
            bool g = lowerBetter ? v <= good : v >= good, o = lowerBetter ? v <= ok : v >= ok;
            return g ? Color.FromArgb(40, 190, 80) : o ? Color.FromArgb(235, 190, 30) : Color.FromArgb(235, 90, 40);
        }

        // texto de avaliacao a partir dos resultados
        string Verdict()
        {
            if (double.IsNaN(hDown) && double.IsNaN(hPing)) return "-";
            List<string> l = new List<string>();
            if (!double.IsNaN(hPing)) l.Add(hPing <= 20 ? "Latência excelente para jogos online" : hPing <= 50 ? "Latência boa para jogos" : hPing <= 100 ? "Latência regular (jogos competitivos podem sofrer)" : "Latência alta");
            if (!double.IsNaN(hDown)) l.Add(hDown >= 100 ? "download ótimo (4K e downloads rápidos)" : hDown >= 25 ? "download bom para streaming em HD" : hDown >= 5 ? "download básico" : "download lento");
            if (!double.IsNaN(hUp) && hUp < 3) l.Add("upload baixo (chamadas de video e envios podem engasgar)");
            return string.Join("; ", l.ToArray()) + ".";
        }

        void LoadHist()
        {
            foreach (string row in Settings.Get("net.hist", "").Split(new string[] { "~|~" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = row.Split('|'); if (f.Length < 5) continue;
                lvHist.Items.Add(new ListViewItem(f));
            }
        }
        void SaveHist()
        {
            List<string> rows = new List<string>();
            foreach (ListViewItem it in lvHist.Items)
            {
                List<string> f = new List<string>(); foreach (ListViewItem.ListViewSubItem x in it.SubItems) f.Add(x.Text.Replace("|", "/").Replace("~", ""));
                rows.Add(string.Join("|", f.ToArray()));
            }
            Settings.Set("net.hist", string.Join("~|~", rows.ToArray()));
        }
        void AddHist()
        {
            ListViewItem it = new ListViewItem(new string[] { DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                double.IsNaN(hPing) ? "-" : hPing.ToString("0") + " ms", double.IsNaN(hJit) ? "-" : hJit.ToString("0.0") + " ms",
                double.IsNaN(hDown) ? "-" : hDown.ToString("0.0") + " Mbps", double.IsNaN(hUp) ? "-" : hUp.ToString("0.0") + " Mbps" });
            lvHist.Items.Insert(0, it);
            while (lvHist.Items.Count > 10) lvHist.Items.RemoveAt(lvHist.Items.Count - 1);
            SaveHist();
        }
        void CopyHist()
        {
            if (lvHist.Items.Count == 0) return;
            StringBuilder sb = new StringBuilder();
            foreach (ListViewItem it in lvHist.Items) { List<string> f = new List<string>(); foreach (ListViewItem.ListViewSubItem x in it.SubItems) f.Add(x.Text); sb.AppendLine(string.Join("\t", f.ToArray())); }
            try { Clipboard.SetText(sb.ToString()); } catch (Exception) { }
        }

        // ---------- teste ----------
        void Toggle()
        {
            if (running) { cancel = true; lblMsg.Text = "Cancelando..."; return; }
            running = true; cancel = false;
            bStart.Text = "Cancelar";
            lblPing.Text = lblJit.Text = lblDown.Text = lblUp.Text = "-"; lblQual.Text = "Medindo...";
            lblPing.Accent = lblJit.Accent = lblDown.Accent = lblUp.Accent = Color.Empty;
            hPing = hJit = hDown = hUp = double.NaN;
            gauge.Reset();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { Run(); });
        }
        void UI2(Action a) { try { BeginInvoke(a); } catch (Exception) { } }

        void Run()
        {
            string err = null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
                ServicePointManager.DefaultConnectionLimit = 32;
                ServicePointManager.Expect100Continue = false;
                // 1) ping
                UI2(delegate { gauge.Caption = "Ping..."; gauge.Unit = "ms"; gauge.Reset(); lblMsg.Text = "Medindo latência..."; });
                List<double> rt = new List<double>();
                for (int i = 0; i < 8 && !cancel; i++)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://speed.cloudflare.com/__down?bytes=0");
                    rq.Timeout = 6000; rq.KeepAlive = true;
                    using (WebResponse rs = rq.GetResponse()) { }
                    sw.Stop();
                    if (i > 0) { rt.Add(sw.Elapsed.TotalMilliseconds); double cur = sw.Elapsed.TotalMilliseconds; UI2(delegate { gauge.Value = cur; }); }
                }
                if (rt.Count > 0)
                {
                    double mn = double.MaxValue, jit = 0; foreach (double d in rt) mn = Math.Min(mn, d);
                    for (int i = 1; i < rt.Count; i++) jit += Math.Abs(rt[i] - rt[i - 1]);
                    jit = rt.Count > 1 ? jit / (rt.Count - 1) : 0;
                    hPing = mn; hJit = jit;
                    UI2(delegate { lblPing.Text = mn.ToString("0") + " ms"; lblJit.Text = jit.ToString("0.0") + " ms"; lblPing.Accent = Q(mn, 30, 80, true); lblJit.Accent = Q(jit, 5, 15, true); });
                }
                // 2) download   3) upload
                if (!cancel) { double d = Phase(false); hDown = d; UI2(delegate { lblDown.Text = d.ToString("0.0") + " Mbps"; lblDown.Accent = Q(d, 50, 10, false); }); }
                if (!cancel) { double u = Phase(true); hUp = u; UI2(delegate { lblUp.Text = u.ToString("0.0") + " Mbps"; lblUp.Accent = Q(u, 20, 5, false); }); }
            }
            catch (Exception ex) { err = ex.Message; }
            UI2(delegate
            {
                tm.Stop(); running = false; bStart.Text = "Iniciar teste";
                gauge.Caption = err != null ? "Falhou" : (cancel ? "Cancelado" : "Concluído"); gauge.Unit = "Mbps";
                lblMsg.Text = err != null ? "Não foi possível medir (sem internet ou bloqueio de rede?): " + err : (cancel ? "Teste cancelado." : "Teste concluído.");
                if (err == null && !cancel) { lblQual.Text = Verdict(); AddHist(); } else lblQual.Text = "-";
            });
        }

        long lastB; DateTime lastT; double ema;
        void Sample()
        {
            long b = System.Threading.Interlocked.Read(ref bytes); DateTime now = DateTime.UtcNow;
            double dt = (now - lastT).TotalSeconds;
            if (dt > 0.05)
            {
                double mbps = (b - lastB) * 8.0 / 1e6 / dt;
                ema = ema <= 0 ? mbps : ema * 0.6 + mbps * 0.4;
                gauge.Value = ema; lastB = b; lastT = now;
            }
        }

        // devolve a media (Mbps) depois de 1,5 s de aquecimento
        double Phase(bool up)
        {
            const int Secs = 10, Warm = 1500, N = 4;
            System.Threading.Interlocked.Exchange(ref bytes, 0); lastB = 0; ema = 0; lastT = DateTime.UtcNow;
            UI2(delegate { gauge.Reset(); gauge.Caption = up ? "Upload" : "Download"; gauge.Unit = "Mbps"; lblMsg.Text = up ? "Medindo upload..." : "Medindo download..."; tm.Start(); });
            DateTime end = DateTime.UtcNow.AddSeconds(Secs);
            List<HttpWebRequest> reqs = new List<HttpWebRequest>(); object rl = new object();
            List<System.Threading.Thread> ths = new List<System.Threading.Thread>();
            for (int i = 0; i < N; i++)
            {
                System.Threading.Thread t = new System.Threading.Thread(delegate ()
                {
                    byte[] buf = new byte[65536]; new Random().NextBytes(buf);
                    while (!cancel && DateTime.UtcNow < end)
                    {
                        try
                        {
                            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(up ? "https://speed.cloudflare.com/__up" : "https://speed.cloudflare.com/__down?bytes=50000000");
                            rq.Timeout = 15000; rq.ReadWriteTimeout = 8000; rq.Proxy = WebRequest.DefaultWebProxy;
                            lock (rl) reqs.Add(rq);
                            if (up)
                            {
                                rq.Method = "POST"; rq.ContentType = "application/octet-stream"; rq.AllowWriteStreamBuffering = false; rq.ContentLength = 25000000;
                                using (Stream s = rq.GetRequestStream())
                                {
                                    long sent = 0;
                                    while (sent < 25000000 && !cancel && DateTime.UtcNow < end) { s.Write(buf, 0, buf.Length); sent += buf.Length; System.Threading.Interlocked.Add(ref bytes, buf.Length); }
                                }
                            }
                            else
                                using (WebResponse rs = rq.GetResponse())
                                using (Stream s = rs.GetResponseStream())
                                {
                                    int n;
                                    while (!cancel && DateTime.UtcNow < end && (n = s.Read(buf, 0, buf.Length)) > 0) System.Threading.Interlocked.Add(ref bytes, n);
                                }
                            rq.Abort();
                        }
                        catch (Exception) { if (cancel || DateTime.UtcNow >= end) break; System.Threading.Thread.Sleep(200); }
                    }
                });
                t.IsBackground = true; t.Start(); ths.Add(t);
            }
            long b0 = 0; DateTime t0 = DateTime.UtcNow.AddMilliseconds(Warm); bool marked = false;
            while (!cancel && DateTime.UtcNow < end)
            {
                if (!marked && DateTime.UtcNow >= t0) { b0 = System.Threading.Interlocked.Read(ref bytes); t0 = DateTime.UtcNow; marked = true; }
                System.Threading.Thread.Sleep(50);
            }
            long b1 = System.Threading.Interlocked.Read(ref bytes); DateTime t1 = DateTime.UtcNow;
            lock (rl) foreach (HttpWebRequest r in reqs) { try { r.Abort(); } catch (Exception) { } }
            foreach (System.Threading.Thread t in ths) t.Join(1500);
            UI2(delegate { tm.Stop(); });
            double secs = (t1 - t0).TotalSeconds;
            return marked && secs > 0.5 ? (b1 - b0) * 8.0 / 1e6 / secs : 0;
        }

        // ---------- informacoes (so ao clicar) ----------
        void ShowInfo()
        {
            if (Dlg.Show("ATENÇÃO: as informações a seguir são PESSOAIS.\n\nVão aparecer seu IP interno e externo, endereços MAC, provedor, cidade aproximada, nome da rede Wi-Fi e DNS.\nNão compartilhe prints desta tela.\n\nMostrar mesmo assim?", "BunoTunner - Dados pessoais", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            bInfo.Enabled = false; lblHint.Text = "Coletando informações..."; lblHint.Visible = true; tbInfo.Visible = false;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string txt;
                try { txt = Gather(); } catch (Exception ex) { txt = "Erro: " + ex.Message; }
                UI2(delegate { tbInfo.Text = txt; tbInfo.Visible = true; lblHint.Visible = false; bInfo.Enabled = true; tbInfo.SelectionStart = 0; });
            });
        }

        static string J(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
        static string Get(string url)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Timeout = 8000; rq.UserAgent = "BunoTunner";
            using (WebResponse rs = rq.GetResponse())
            using (StreamReader sr = new StreamReader(rs.GetResponseStream())) return sr.ReadToEnd();
        }
        static string Gather()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Este computador ===");
            sb.AppendLine("Nome do PC: " + Environment.MachineName);
            sb.AppendLine("Usuário: " + Environment.UserDomainName + "\\" + Environment.UserName);
            foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                System.Net.NetworkInformation.IPInterfaceProperties ip = ni.GetIPProperties();
                sb.AppendLine();
                sb.AppendLine("--- Adaptador: " + ni.Name + " ---");
                sb.AppendLine("Descrição: " + ni.Description);
                sb.AppendLine("Tipo: " + ni.NetworkInterfaceType + "   Velocidade do link: " + (ni.Speed / 1000000) + " Mbps");
                string mac = ni.GetPhysicalAddress().ToString();
                if (mac.Length == 12) mac = Regex.Replace(mac, "(.{2})(?!$)", "$1-");
                sb.AppendLine("MAC: " + mac);
                foreach (System.Net.NetworkInformation.UnicastIPAddressInformation u in ip.UnicastAddresses)
                    sb.AppendLine((u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "IPv4: " : "IPv6: ") + u.Address + (u.IPv4Mask != null && u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "   máscara " + u.IPv4Mask : ""));
                foreach (System.Net.NetworkInformation.GatewayIPAddressInformation gw in ip.GatewayAddresses) sb.AppendLine("Gateway: " + gw.Address);
                foreach (System.Net.IPAddress d in ip.DnsAddresses) sb.AppendLine("DNS: " + d);
                foreach (System.Net.IPAddress d in ip.DhcpServerAddresses) sb.AppendLine("Servidor DHCP: " + d);
                if (!string.IsNullOrEmpty(ip.DnsSuffix)) sb.AppendLine("Sufixo DNS: " + ip.DnsSuffix);
            }
            int c;
            string wl = Proc.Run("netsh.exe", "wlan show interfaces", 6000, out c);
            if (c == 0 && wl.Length > 20) { sb.AppendLine(); sb.AppendLine("=== Wi-Fi ==="); sb.AppendLine(wl); }
            sb.AppendLine();
            sb.AppendLine("=== Internet (visto de fora) ===");
            try
            {
                string j = Get("https://ipinfo.io/json");
                sb.AppendLine("IP externo: " + J(j, "ip"));
                string hn = J(j, "hostname"); if (hn.Length > 0) sb.AppendLine("Host reverso: " + hn);
                sb.AppendLine("Provedor (ISP/ASN): " + J(j, "org"));
                sb.AppendLine("Cidade: " + J(j, "city") + " - " + J(j, "region") + " - " + J(j, "country") + "  CEP: " + J(j, "postal"));
                sb.AppendLine("Localização aproximada: " + J(j, "loc"));
                sb.AppendLine("Fuso horário: " + J(j, "timezone"));
            }
            catch (Exception ex) { sb.AppendLine("Não foi possível consultar o IP externo: " + ex.Message); }
            try { string v6 = Get("https://api64.ipify.org").Trim(); sb.AppendLine("IP preferido (v6 se houver): " + v6); } catch (Exception) { }
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------
    //  Saude de armazenamento: vida util restante, temperatura e S.M.A.R.T. (NVMe e SATA)
    // ------------------------------------------------------------------
    class DiskInfo
    {
        public string Id = "", Name = "", Media = "", Bus = "", Health = "", Fw = "";
        public long Size;
        public double Temp = double.NaN, TempMax = double.NaN, Wear = double.NaN, Hours = double.NaN, ReadErr = double.NaN, WriteErr = double.NaN, Cycles = double.NaN;
        public bool IsSsd { get { return Media == "SSD" || Bus == "NVMe"; } }
        public class Attr { public int Id; public int Val, Worst, Thr; public long Raw; }
        public List<Attr> Smart = new List<Attr>();
        public bool Predict;                 // o proprio disco previu falha (FailurePredictStatus)
        public string Reason = "";           // motivo do estado Ruim/Atencao, vindo dos atributos S.M.A.R.T.
        static readonly Dictionary<int, string> AttrNames = new Dictionary<int, string> {
            {1,"Taxa de erros de leitura"},{2,"Desempenho (throughput)"},{3,"Tempo de spin-up"},{4,"Partidas/paradas"},{5,"Setores realocados"},{7,"Taxa de erros de busca"},{9,"Horas ligado"},
            {10,"Tentativas de spin-up"},{11,"Tentativas de recalibração"},{12,"Ciclos liga/desliga"},{183,"Blocos ruins em execução"},{184,"Erros fim-a-fim"},{187,"Erros incorrigíveis reportados"},
            {188,"Timeouts de comando"},{189,"Gravações com cabeça alta"},{190,"Temperatura do fluxo de ar"},{191,"Choques (G-sense)"},{192,"Retrações por desligamento"},{193,"Ciclos de carga/descarga"},
            {194,"Temperatura"},{195,"Erros de ECC corrigidos"},{196,"Eventos de realocação"},{197,"Setores pendentes"},{198,"Setores incorrigíveis offline"},{199,"Erros de CRC (cabo)"},{200,"Erros multi-zona"},
            {240,"Horas de cabeça"},{241,"Total gravado (LBAs)"},{242,"Total lido (LBAs)"} };
        public static string AttrName(int id) { string s; return AttrNames.TryGetValue(id, out s) ? s : "Atributo"; }
        public void Judge(out bool bad, out bool warn)
        {
            bad = Predict; warn = false; List<string> why = new List<string>();
            if (Predict) why.Add("o disco previu falha (S.M.A.R.T.)");
            foreach (Attr a in Smart)
            {
                long r = Raw32(a);
                if (a.Thr > 0 && a.Val > 0 && a.Val <= a.Thr && a.Id != 190 && a.Id != 194) { bad = true; why.Add(AttrName(a.Id) + " abaixo do limite"); }
                switch (a.Id)
                {
                    case 5: case 196: if (r >= 100) { bad = true; why.Add(r + " " + AttrName(a.Id).ToLower()); } else if (r > 0) { warn = true; why.Add(r + " " + AttrName(a.Id).ToLower()); } break;
                    case 197: case 198: case 187: if (r >= 10) { bad = true; why.Add(r + " " + AttrName(a.Id).ToLower()); } else if (r > 0) { warn = true; why.Add(r + " " + AttrName(a.Id).ToLower()); } break;
                    case 10: case 184: case 183: case 199: if (r > 0) { warn = true; why.Add(r + " - " + AttrName(a.Id).ToLower()); } break;
                }
            }
            Reason = string.Join("; ", why.ToArray());
        }
        static long Raw32(Attr a) { return a.Raw & 0xFFFFFFFFL; }
        public double Life { get { return double.IsNaN(Wear) ? double.NaN : Math.Max(0, 100 - Wear); } }
        public string Kind { get { return (Bus.Length > 0 ? Bus + " " : "") + (Media == "SSD" ? "SSD" : Media == "HDD" ? "HDD" : Media); } }
    }

    class StoragePage : Panel
    {
        ListView lvD = new XpList(), lvA = new XpList();
        Label lblHealth, lblLife, lblTemp, lblAdvice, lblMsg;
        XpProgress pbLife, pbTemp;
        XpOption chkAlert; XpText txFilter; List<string[]> allRows = new List<string[]>();
        List<DiskInfo> disks = new List<DiskInfo>();
        DiskInfo cur; bool loading, loadedOnce;
        Timer tm = new Timer();

        public StoragePage()
        {
            Size = new Size(676, 586); Dock = DockStyle.Fill;   // tamanho inicial = o da area da pagina, para os ancoramentos funcionarem
            XpGroup g1 = new XpGroup(); g1.Title = "Seus discos"; g1.SetBounds(0, 0, 676, 156); g1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            ((XpList)lvD).AutoFit = true; lvD.SetBounds(12, 24, 652, 88); lvD.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; lvD.View = View.Details; lvD.FullRowSelect = true; lvD.GridLines = true; lvD.HideSelection = false; lvD.MultiSelect = false;
            lvD.BackColor = Xp.EditBg; lvD.Font = Xp.Ui;
            lvD.Columns.Add("Disco", 232); lvD.Columns.Add("Tipo", 90); lvD.Columns.Add("Tamanho", 70); lvD.Columns.Add("Saúde", 70); lvD.Columns.Add("Vida", 60); lvD.Columns.Add("Temp.", 60); lvD.Columns.Add("Horas", 60);
            UI.DoubleBuffer(lvD);
            lvD.SelectedIndexChanged += delegate { cur = lvD.SelectedItems.Count > 0 ? lvD.SelectedItems[0].Tag as DiskInfo : null; ShowDetail(); };
            g1.Controls.Add(lvD);
            UI.Btn(g1, "Atualizar", 12, 120, 90, delegate { Load(); });
            UI.Btn(g1, "Copiar relatório", 108, 120, 120, delegate { CopyReport(); });
            UI.Btn(g1, "Salvar relatório...", 234, 120, 130, delegate { SaveReport(); });
            lblMsg = UI.Lbl(g1, "", 374, 118, Xp.Ui, Xp.Dim);
            lblMsg.AutoSize = false; lblMsg.SetBounds(374, 116, 290, 32);
            Controls.Add(g1);

            XpGroup g2 = new XpGroup(); g2.Title = "Detalhes do disco selecionado"; g2.SetBounds(0, 162, 676, 424); g2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            UI.Lbl(g2, "Saúde geral:", 14, 28, Xp.UiBold);
            lblHealth = UI.Lbl(g2, "-", 130, 24, Xp.Big, Xp.GroupText);
            UI.Lbl(g2, "Vida útil restante:", 14, 62, Xp.UiBold);
            pbLife = new XpProgress(); pbLife.SetBounds(150, 60, 330, 18); g2.Controls.Add(pbLife);
            lblLife = UI.Lbl(g2, "-", 490, 62, Xp.UiBold, Xp.GroupText);
            UI.Lbl(g2, "Temperatura:", 14, 92, Xp.UiBold);
            pbTemp = new XpProgress(); pbTemp.SetBounds(150, 90, 330, 18); g2.Controls.Add(pbTemp);
            lblTemp = UI.Lbl(g2, "-", 490, 92, Xp.UiBold, Xp.GroupText);
            lblAdvice = UI.Lbl(g2, "Selecione um disco na lista acima.", 14, 120, Xp.Ui, Xp.Dim);
            lblAdvice.AutoSize = false; lblAdvice.SetBounds(14, 120, 648, 44);
            chkAlert = UI.Opt(g2, "Mostrar só alertas", 14, 166, 170, false);
            UI.Lbl(g2, "Filtrar:", 220, 170);
            txFilter = UI.Txt(g2, 266, 165, 200, "");
            chkAlert.CheckedChanged += delegate { FillAttrs(); };
            txFilter.Box.TextChanged += delegate { FillAttrs(); };
            lvA.SetBounds(12, 194, 652, 220); lvA.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom; lvA.View = View.Details; lvA.FullRowSelect = true; lvA.GridLines = true;
            lvA.BackColor = Xp.EditBg; lvA.Font = Xp.Ui;
            lvA.Columns.Add("Atributo / sensor", 290); lvA.Columns.Add("Valor", 270); lvA.Columns.Add("Situação", 80);
            UI.DoubleBuffer(lvA);
            g2.Controls.Add(lvA);
            Controls.Add(g2);

            VisibleChanged += delegate { if (Visible && !loadedOnce) { loadedOnce = true; Load(); } tm.Enabled = Visible; };
            tm.Interval = 3000; tm.Tick += delegate { LiveTemp(); };
        }

        static double D(string s) { double v; return double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN; }

        void Load()
        {
            if (loading) return;
            loading = true; lblMsg.Text = "Lendo os discos (Windows Storage + S.M.A.R.T.)...";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<DiskInfo> l = new List<DiskInfo>(); string err = null;
                try
                {
                    int c;
                    string cmd = "-NoProfile -NonInteractive -Command \"Get-PhysicalDisk | ForEach-Object { $r = $_ | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue; ('DISK',$_.DeviceId,$_.FriendlyName,$_.MediaType,$_.BusType,$_.HealthStatus,$_.Size,$_.FirmwareVersion,$r.Temperature,$r.TemperatureMax,$r.Wear,$r.PowerOnHours,$r.ReadErrorsTotal,$r.WriteErrorsTotal,$r.StartStopCycleCount) -join '|' }\"";
                    string o = Proc.Run("powershell.exe", cmd, 45000, out c);
                    foreach (string line in o.Replace("\r", "").Split('\n'))
                    {
                        if (!line.StartsWith("DISK|")) continue;
                        string[] p = line.Split('|');
                        if (p.Length < 15) continue;
                        DiskInfo d = new DiskInfo();
                        d.Id = p[1]; d.Name = p[2].Trim(); d.Media = p[3].Trim(); d.Bus = p[4].Trim(); d.Health = p[5].Trim(); d.Fw = p[7].Trim();
                        long sz; long.TryParse(p[6], out sz); d.Size = sz;
                        d.Temp = D(p[8]); d.TempMax = D(p[9]); d.Wear = D(p[10]); d.Hours = D(p[11]); d.ReadErr = D(p[12]); d.WriteErr = D(p[13]); d.Cycles = D(p[14]);
                        if (d.Temp <= 0) d.Temp = double.NaN;
                        l.Add(d);
                    }
                    if (l.Count == 0) err = o.Length > 0 && !o.StartsWith("DISK") ? o : "o Windows não informou discos";
                    // atributos S.M.A.R.T. reais (WMI root\wmi): o 'Healthy' do Windows quase nunca acusa um HDD ruim
                    try
                    {
                        string ps = "$d=@{};$t=@{};$f=@{};"
                          + "Get-CimInstance -Namespace root\\wmi -ClassName MSStorageDriver_ATAPISmartData -ErrorAction SilentlyContinue | ForEach-Object { $d[$_.InstanceName.ToUpper()]=$_.VendorSpecific };"
                          + "Get-CimInstance -Namespace root\\wmi -ClassName MSStorageDriver_FailurePredictThresholds -ErrorAction SilentlyContinue | ForEach-Object { $t[$_.InstanceName.ToUpper()]=$_.VendorSpecific };"
                          + "Get-CimInstance -Namespace root\\wmi -ClassName MSStorageDriver_FailurePredictStatus -ErrorAction SilentlyContinue | ForEach-Object { $f[$_.InstanceName.ToUpper()]=$_.PredictFailure };"
                          + "Get-CimInstance Win32_DiskDrive | ForEach-Object { $p=$_.PNPDeviceID.ToUpper(); $k=$d.Keys | Where-Object { $_.StartsWith($p) } | Select-Object -First 1; $out=@(); $v=$null; if($k){$v=$d[$k]}; "
                          + "if($v){ for($i=2;$i+11 -lt $v.Length;$i+=12){ $id=$v[$i]; if($id -eq 0){continue}; $raw=[int64]0; for($j=5;$j -ge 0;$j--){ $raw=$raw*256+$v[$i+5+$j] }; $th=0; if($t[$k]){$th=$t[$k][$i+1]}; $out+=('{0}:{1}:{2}:{3}:{4}' -f $id,$v[$i+3],$v[$i+4],$th,$raw) } }; "
                          + "('SMART',$_.Index,$f[$k],($out -join ';')) -join '|' }";
                        string so = Proc.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"" + ps + "\"", 45000, out c);
                        foreach (string line in so.Replace("\r", "").Split('\n'))
                        {
                            if (!line.StartsWith("SMART|")) continue;
                            string[] p = line.Split('|'); if (p.Length < 4) continue;
                            DiskInfo d = null; foreach (DiskInfo x in l) if (x.Id == p[1]) d = x;
                            if (d == null) continue;
                            d.Predict = p[2].Equals("True", StringComparison.OrdinalIgnoreCase);
                            foreach (string e in p[3].Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                string[] q = e.Split(':'); if (q.Length < 5) continue;
                                DiskInfo.Attr a = new DiskInfo.Attr(); int iv; long lv;
                                int.TryParse(q[0], out iv); a.Id = iv; int.TryParse(q[1], out iv); a.Val = iv; int.TryParse(q[2], out iv); a.Worst = iv; int.TryParse(q[3], out iv); a.Thr = iv; long.TryParse(q[4], out lv); a.Raw = lv;
                                d.Smart.Add(a);
                            }
                        }
                    }
                    catch (Exception) { }
                }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        loading = false; disks = l; FillList();
                        lblMsg.Text = err != null ? "Não foi possível ler: " + err : l.Count + " disco(s). Alguns discos USB/RAID não informam S.M.A.R.T. ao Windows.";
                    }));
                }
                catch (Exception) { }
            });
        }

        static string HealthText(DiskInfo d)
        {
            double life = d.Life; bool sb, sw; d.Judge(out sb, out sw);
            if (sb || d.Health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase) || (!double.IsNaN(life) && life < 10)) return "Ruim";
            if (sw || d.Health.Equals("Warning", StringComparison.OrdinalIgnoreCase) || (!double.IsNaN(life) && life < 30) || (!double.IsNaN(d.Temp) && d.Temp >= 70) || (!double.IsNaN(d.ReadErr) && d.ReadErr > 0) || (!double.IsNaN(d.WriteErr) && d.WriteErr > 0)) return "Atenção";
            if ((d.Health.Length == 0 || d.Health.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) && d.Smart.Count == 0) return "Sem dados";
            return "Boa";
        }
        Color HealthColor(string h) { return h == "Boa" ? Xp.Good : h == "Sem dados" ? Xp.Dim : Xp.Bad; }

        string Fmt(double v, string unit) { return double.IsNaN(v) ? "-" : v.ToString("0.#") + unit; }

        void FillList()
        {
            string keep = cur != null ? cur.Id : null;
            lvD.Items.Clear();
            foreach (DiskInfo d in disks)
            {
                string h = HealthText(d);
                ListViewItem it = new ListViewItem(d.Name);
                it.SubItems.Add(d.Kind); it.SubItems.Add((d.Size / 1073741824L) + " GB"); it.SubItems.Add(h);
                it.SubItems.Add(double.IsNaN(d.Life) ? "-" : d.Life.ToString("0") + "%"); it.SubItems.Add(Fmt(d.Temp, " C")); it.SubItems.Add(double.IsNaN(d.Hours) ? "-" : d.Hours.ToString("0"));
                it.Tag = d; it.SubItems[3].ForeColor = HealthColor(h);
                lvD.Items.Add(it);
                if (keep != null && d.Id == keep) it.Selected = true;
            }
            if (lvD.SelectedItems.Count == 0 && lvD.Items.Count > 0) lvD.Items[0].Selected = true;
        }

        // sensores do LibreHardwareMonitor para o disco (temperatura ao vivo, vida, dados gravados...)
        static string Norm(string s) { return Regex.Replace((s ?? "").ToLower(), "[^a-z0-9]", ""); }
        List<Sn> Sensors(DiskInfo d)
        {
            List<Sn> r = new List<Sn>(); string n = Norm(d.Name);
            if (n.Length == 0) return r;
            foreach (Sn s in SensorHub.PcSensors)
                if (s.Group.StartsWith("Armazenamento") && (Norm(s.Group).Contains(n) || n.Contains(Norm(s.Group.Substring(Math.Min(s.Group.Length, 24)))) )) r.Add(s);
            return r;
        }

        void ShowDetail()
        {
            lvA.Items.Clear();
            if (cur == null) { lblHealth.Text = "-"; lblLife.Text = "-"; lblTemp.Text = "-"; pbLife.Value = 0; pbTemp.Value = 0; allRows.Clear(); return; }
            DiskInfo d = cur; string h = HealthText(d);
            lblHealth.Text = h; lblHealth.ForeColor = HealthColor(h);
            if (double.IsNaN(d.Life)) { pbLife.Value = 0; lblLife.Text = d.IsSsd ? "indisponível" : "n/a (HDD)"; } else { pbLife.Value = (int)d.Life; lblLife.Text = d.Life.ToString("0") + "% restante"; }
            LiveTemp();
            List<string[]> rows = new List<string[]>();
            rows.Add(new string[] { "Modelo", d.Name }); rows.Add(new string[] { "Tipo / interface", d.Kind });
            rows.Add(new string[] { "Capacidade", (d.Size / 1000000000L) + " GB (" + (d.Size / 1073741824L) + " GiB)" });
            rows.Add(new string[] { "Firmware", d.Fw }); rows.Add(new string[] { "Estado informado pelo Windows", d.Health });
            if (d.Smart.Count == 0) rows.Add(new string[] { "S.M.A.R.T. detalhado", "não disponível (disco USB/RAID ou NVMe sem tabela ATA)" });
            if (!double.IsNaN(d.Wear)) rows.Add(new string[] { "Desgaste (uso da vida útil)", d.Wear.ToString("0") + "%" });
            if (!double.IsNaN(d.Hours)) rows.Add(new string[] { "Horas ligado", d.Hours.ToString("0") + " h (~" + (d.Hours / 24).ToString("0") + " dias)" });
            if (!double.IsNaN(d.Cycles)) rows.Add(new string[] { "Ciclos liga/desliga", d.Cycles.ToString("0") });
            if (!double.IsNaN(d.TempMax)) rows.Add(new string[] { "Temperatura máxima registrada", d.TempMax.ToString("0") + " C" });
            rows.Add(new string[] { "Erros de leitura (total)", Fmt(d.ReadErr, "") }); rows.Add(new string[] { "Erros de escrita (total)", Fmt(d.WriteErr, "") });
            foreach (DiskInfo.Attr a in d.Smart)
            {
                if (a.Id == 194 || a.Id == 190 || a.Id == 9) continue;
                rows.Add(new string[] { "S.M.A.R.T. " + a.Id + " - " + DiskInfo.AttrName(a.Id), "bruto " + (a.Raw & 0xFFFFFFFFL) + "  (atual " + a.Val + " / pior " + a.Worst + (a.Thr > 0 ? " / limite " + a.Thr : "") + ")" });
            }
            foreach (Sn s in Sensors(d))
            {
                float? v = s.Last; if (!v.HasValue) continue;
                string unit = s.Type == "Temperature" ? " C" : s.Type == "Load" || s.Type == "Level" ? " %" : s.Type == "Data" ? " GB" : s.Type == "Throughput" ? " B/s" : "";
                rows.Add(new string[] { "S.M.A.R.T. - " + s.Name, v.Value.ToString("0.##") + unit });
            }
            allRows.Clear();
            foreach (string[] r in rows)
            {
                string st = "";
                if (r[0].StartsWith("S.M.A.R.T. ") && r[1].StartsWith("bruto"))
                {
                    Match mm = Regex.Match(r[0], "S\\.M\\.A\\.R\\.T\\. (\\d+) "); int id = mm.Success ? int.Parse(mm.Groups[1].Value) : 0;
                    DiskInfo.Attr at = null; foreach (DiskInfo.Attr x in d.Smart) if (x.Id == id) at = x;
                    st = "OK";
                    if (at != null)
                    {
                        if (at.Thr > 0 && at.Val <= at.Thr) st = "Ruim";
                        else if ((id == 5 || id == 10 || id == 184 || id == 187 || id == 188 || id == 196 || id == 197 || id == 198) && (at.Raw & 0xFFFFFFFFL) > 0) st = "Atenção";
                    }
                }
                else if ((r[0].StartsWith("Erros de leitura") || r[0].StartsWith("Erros de escrita")) && r[1] != "-" && r[1] != "0") st = "Atenção";
                allRows.Add(new string[] { r[0], r[1], st });
            }
            FillAttrs();
            string adv;
            if (h == "Ruim") adv = "ATENÇÃO: o disco indica falha iminente ou pouca vida útil" + (d.Reason.Length > 0 ? " (" + d.Reason + ")" : "") + ". Faça backup AGORA e planeje a troca.";
            else if (h == "Atenção") adv = "Ha sinais de desgaste, erros ou temperatura alta" + (d.Reason.Length > 0 ? " (" + d.Reason + ")" : "") + ". Mantenha backup em dia e acompanhe.";
            else if (h == "Boa") adv = d.IsSsd ? "Tudo certo. SSDs perdem vida conforme a quantidade de dados gravados; valores abaixo de 30% pedem atenção." : "Tudo certo. Em HDDs, observe erros e temperatura (ideal abaixo de 50 C).";
            else adv = "O Windows não informou S.M.A.R.T. deste disco (comum em discos USB e controladoras RAID).";
            lblAdvice.Text = adv; lblAdvice.ForeColor = h == "Ruim" || h == "Atenção" ? Xp.Bad : Xp.Dim;
        }

        void FillAttrs()
        {
            string f = (txFilter == null ? "" : txFilter.Text).Trim();
            bool only = chkAlert != null && chkAlert.Checked;
            lvA.BeginUpdate(); lvA.Items.Clear();
            foreach (string[] r in allRows)
            {
                if (only && r[2] != "Ruim" && r[2] != "Atenção") continue;
                if (f.Length > 0 && r[0].IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 && r[1].IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                ListViewItem it = new ListViewItem(new string[] { r[0], r[1], r[2] });
                if (r[2] == "Ruim") it.ForeColor = Xp.Bad; else if (r[2] == "Atenção") it.ForeColor = Color.FromArgb(214, 140, 0);
                lvA.Items.Add(it);
            }
            lvA.EndUpdate();
        }

        string Report()
        {
            if (cur == null) return "";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("BunoTunner - relatório de saúde do disco  (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
            sb.AppendLine("Disco: " + cur.Name + "   Saúde geral: " + HealthText(cur));
            sb.AppendLine(lblLife.Text.Length > 0 ? "Vida útil: " + lblLife.Text + "   Temperatura: " + lblTemp.Text : "");
            sb.AppendLine(lblAdvice.Text); sb.AppendLine();
            foreach (string[] r in allRows) sb.AppendLine(r[0] + ": " + r[1] + (r[2].Length > 0 && r[2] != "OK" ? "   [" + r[2] + "]" : ""));
            return sb.ToString();
        }
        void CopyReport()
        {
            if (cur == null) { lblMsg.Text = "Selecione um disco primeiro."; return; }
            try { Clipboard.SetText(Report()); lblMsg.Text = "Relatório copiado para a área de transferência."; } catch (Exception ex) { lblMsg.Text = "Não foi possível copiar: " + ex.Message; }
        }
        void SaveReport()
        {
            if (cur == null) { lblMsg.Text = "Selecione um disco primeiro."; return; }
            using (SaveFileDialog sd = new SaveFileDialog())
            {
                sd.Filter = "Texto (*.txt)|*.txt"; sd.FileName = "saúde-disco-" + Regex.Replace(cur.Name, "[^A-Za-z0-9]+", "_") + ".txt";
                if (sd.ShowDialog(FindForm()) != DialogResult.OK) return;
                try { File.WriteAllText(sd.FileName, Report(), Encoding.UTF8); lblMsg.Text = "Relatório salvo."; } catch (Exception ex) { lblMsg.Text = "Não foi possível salvar: " + ex.Message; }
            }
        }

        void LiveTemp()
        {
            if (cur == null) return;
            double t = cur.Temp;
            foreach (Sn s in Sensors(cur)) if (s.Type == "Temperature" && s.IsRealTemp && s.Last.HasValue) { t = s.Last.Value; break; }
            if (double.IsNaN(t)) { pbTemp.Value = 0; lblTemp.Text = "indisponível"; return; }
            pbTemp.Value = (int)Math.Max(0, Math.Min(100, t)); lblTemp.Text = t.ToString("0") + " C";
            lblTemp.ForeColor = t >= 70 ? Xp.Bad : Xp.GroupText;
        }
    }

    // ------------------------------------------------------------------
    //  Atualizacao de drivers: inventario (WMI) + Windows Update Agent (COM) com verificacao de assinatura e hash SHA-256
    // ------------------------------------------------------------------
    static class DriverUpdater
    {
        static object Gp(object o, string n) { return o.GetType().InvokeMember(n, BindingFlags.GetProperty, null, o, null); }
        static void Sp(object o, string n, object v) { o.GetType().InvokeMember(n, BindingFlags.SetProperty, null, o, new object[] { v }); }
        static object Cm(object o, string n, params object[] a) { return o.GetType().InvokeMember(n, BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, o, a); }   // "Item" e propriedade indexada no COM
        static object New(string progId) { return Activator.CreateInstance(Type.GetTypeFromProgID(progId, true)); }
        static string S(object o) { return o == null ? "" : o.ToString().Trim(); }

        // desembrulha a excecao do COM e traduz os codigos mais comuns do Windows Update
        static string Why(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            int hr = Marshal.GetHRForException(ex);
            string hex = "0x" + hr.ToString("X8"), hint = "";
            switch ((uint)hr)
            {
                case 0x80070422: hint = "O serviço Windows Update esta desativado."; break;
                case 0x8024402C: case 0x80072EE7: case 0x80072EFD: case 0x80072EE2: case 0x8024401C: hint = "Sem acesso ao servidor do Windows Update (internet, DNS, proxy ou firewall)."; break;
                case 0x80240438: case 0x8024001E: hint = "O serviço Windows Update não está pronto; reinicie o Windows e tente de novo."; break;
                case 0x80070005: hint = "Acesso negado; execute o programa como administrador."; break;
                case 0x80240024: hint = "O Windows Update não tem drivers para oferecer."; break;
                case 0x8024A105: case 0x80244022: case 0x80244010: hint = "O servidor do Windows Update esta ocupado; tente em alguns minutos."; break;
            }
            return ex.Message.Trim() + " (" + hex + ")" + (hint.Length > 0 ? " - " + hint : "");
        }

        // garante que o servico do Windows Update esteja ligado
        static void EnsureWuService(Action<string> log)
        {
            int c;
            string q = Proc.Run("sc.exe", "qc wuauserv", 15000, out c);
            if (q.IndexOf("DISABLED", StringComparison.OrdinalIgnoreCase) >= 0 || q.IndexOf("DESATIVADO", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                log("  O serviço Windows Update está desativado; ativando para esta operação...");
                Proc.Run("sc.exe", "config wuauserv start= demand", 15000, out c);
            }
            Proc.Run("sc.exe", "start wuauserv", 15000, out c);
            Proc.Run("sc.exe", "start bits", 15000, out c);
        }

        static string LogPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BunoTunner_drivers.log"); } }
        static void ToFile(string s) { try { File.AppendAllText(LogPath, s + "\r\n", new UTF8Encoding(true)); } catch (Exception) { } }

        static string Ps(string script, int ms)
        {
            int c;
            return Proc.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"" + script.Replace("\"", "\\\"") + "\"", ms, out c);
        }

        // inventario: todos os dispositivos com driver e os que estao com problema
        static void Inventory(Action<string> log)
        {
            int total = 0, unsigned = 0, bad = 0;
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT DeviceName, IsSigned FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL"))
                    foreach (ManagementBaseObject o in s.Get())
                    {
                        total++;
                        object sg = o["IsSigned"];
                        if (sg is bool && !(bool)sg) { unsigned++; log("  Sem assinatura: " + S(o["DeviceName"])); }
                    }
            }
            catch (Exception ex) { log("  (inventário WMI falhou: " + ex.Message + ")"); }
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
                    foreach (ManagementBaseObject o in s.Get())
                    {
                        bad++;
                        log("  Com problema (código " + S(o["ConfigManagerErrorCode"]) + "): " + S(o["Name"]));
                    }
            }
            catch (Exception) { }
            log("Dispositivos com driver: " + total + "   sem assinatura: " + unsigned + "   com problema: " + bad);
        }

        public class Found { public object Upd; public string Title, Maker, Cls, Date; }

        // Retorna true se terminou (ou se nao havia nada); confirm(lista) decide se instala
        public static void Run(bool install, Action<string> log, Func<string, bool> confirm, Action<string> done)
        {
            string result = "";
            try
            {
                DateTime started = DateTime.Now;
                ToFile("=== " + started.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
                Action<string> L = delegate(string s) { ToFile(s); log(s); };
                L("1/6  Analisando os dispositivos do sistema...");
                Inventory(L);

                L("2/6  Procurando drivers mais novos no Windows Update (pode levar alguns minutos)...");
                EnsureWuService(L);
                object session = New("Microsoft.Update.Session");
                object searcher = null, sr = null;
                int[] servers = { 2, 0 };   // primeiro direto no Windows Update (Microsoft), depois o padrao do PC
                string lastErr = "";
                foreach (int sv in servers)
                {
                    try
                    {
                        searcher = Cm(session, "CreateUpdateSearcher");
                        try { Sp(searcher, "ServerSelection", sv); } catch (Exception) { }
                        sr = Cm(searcher, "Search", "IsInstalled=0 and Type='Driver' and IsHidden=0");
                        break;
                    }
                    catch (Exception ex) { lastErr = Why(ex); L("  Busca falhou (" + (sv == 2 ? "Windows Update" : "servidor padrão") + "): " + lastErr); sr = null; }
                }
                if (sr == null) { result = "Não foi possível consultar o Windows Update: " + lastErr; L(result); done(result); return; }
                object ups = Gp(sr, "Updates");
                int n = Convert.ToInt32(Gp(ups, "Count"));
                if (n == 0) { result = "Todos os drivers já estão na versão mais nova oferecida pelo Windows Update."; L(result); done(result); return; }

                List<Found> list = new List<Found>();
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    object u = Cm(ups, "Item", i);
                    Found f = new Found(); f.Upd = u; f.Title = S(Gp(u, "Title")); f.Maker = S(Gp(u, "DriverManufacturer")); f.Cls = S(Gp(u, "DriverClass"));
                    try { f.Date = Convert.ToDateTime(Gp(u, "DriverVerDate")).ToString("yyyy-MM-dd"); } catch (Exception) { f.Date = ""; }
                    list.Add(f);
                    string line = "  " + f.Title + (f.Date.Length > 0 ? "  [" + f.Date + "]" : "");
                    L(line); sb.AppendLine("- " + f.Title + (f.Date.Length > 0 ? " (" + f.Date + ")" : ""));
                }
                L("Encontrados: " + n + " driver(s) mais novo(s).");
                if (!install) { result = "Verificação concluída: " + n + " driver(s) podem ser atualizados."; done(result); return; }

                if (!confirm(sb.ToString())) { result = "Cancelado pelo usuário. Nada foi instalado."; L(result); done(result); return; }

                L("3/6  Criando ponto de restauração do sistema...");
                string rp = Ps("try { Checkpoint-Computer -Description 'BunoTunner - antes de atualizar drivers' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'OK' } catch { 'FALHA: ' + $_.Exception.Message }", 180000);
                L(rp.Contains("OK") ? "  Ponto de restauração criado." : "  Não foi possível criar o ponto de restauração (" + (rp.Length > 120 ? rp.Substring(0, 120) : rp) + "). Continuando.");

                L("4/6  Baixando os drivers...");
                object coll = New("Microsoft.Update.UpdateColl");
                foreach (Found f in list)
                {
                    try { if (!Convert.ToBoolean(Gp(f.Upd, "EulaAccepted"))) Cm(f.Upd, "AcceptEula"); } catch (Exception) { }
                    Cm(coll, "Add", f.Upd);
                }
                object dl = Cm(session, "CreateUpdateDownloader");
                Sp(dl, "Updates", coll);
                object dr = Cm(dl, "Download");
                int drc = Convert.ToInt32(Gp(dr, "ResultCode"));
                if (drc != 2 && drc != 3) { result = "Falha no download (código " + drc + "). Nada foi instalado."; L(result); done(result); return; }

                L("5/6  Verificando assinatura digital e hash SHA-256 dos arquivos baixados...");
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");
                string script = "$ErrorActionPreference='SilentlyContinue'; $t=[datetime]'" + started.AddSeconds(-5).ToString("yyyy-MM-dd HH:mm:ss") + "'; " +
                    "Get-ChildItem -LiteralPath '" + cache + "' -Recurse -File | Where-Object { $_.LastWriteTime -ge $t } | ForEach-Object { " +
                    "$s=(Get-AuthenticodeSignature -LiteralPath $_.FullName).Status.ToString(); $h=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; $s+'|'+$h+'|'+$_.Name }";
                string vout = Ps(script, 600000);
                int ok = 0, tampered = 0, other = 0;
                foreach (string ln in vout.Split('\n'))
                {
                    string[] p = ln.Trim().Split('|');
                    if (p.Length < 3) continue;
                    ToFile("  SHA256 " + p[1] + "  " + p[2] + "  assinatura: " + p[0]);
                    if (p[0] == "Valid") ok++;
                    else if (p[0] == "HashMismatch" || p[0] == "NotTrusted") { tampered++; L("  ALERTA: " + p[2] + " -> " + p[0]); }
                    else other++;
                }
                L("  Assinaturas validas: " + ok + "   sem assinatura própria (verificados pelo Windows Update): " + other + "   alterados/não confiáveis: " + tampered);
                if (tampered > 0) { result = "ABORTADO: " + tampered + " arquivo(s) com hash/assinatura inválidos. Nada foi instalado."; L(result); done(result); return; }

                L("6/6  Instalando...");
                object inst = Cm(session, "CreateUpdateInstaller");
                Sp(inst, "Updates", coll);
                try { Sp(inst, "AllowSourcePrompts", false); } catch (Exception) { }
                try { Sp(inst, "ForceQuiet", true); } catch (Exception) { }
                object ir = Cm(inst, "Install");
                int good = 0, fail = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    object ur = Cm(ir, "GetUpdateResult", i);
                    int rc = Convert.ToInt32(Gp(ur, "ResultCode"));
                    if (rc == 2 || rc == 3) { good++; L("  Instalado: " + list[i].Title); }
                    else { fail++; L("  FALHOU (código " + rc + ", 0x" + Convert.ToInt32(Gp(ur, "HResult")).ToString("X8") + "): " + list[i].Title); }
                }
                bool reboot = false;
                try { reboot = Convert.ToBoolean(Gp(ir, "RebootRequired")); } catch (Exception) { }
                result = "Concluído: " + good + " instalado(s), " + fail + " com falha." + (reboot ? " REINICIE o Windows para terminar." : "");
                L(result);
                done(result);
            }
            catch (Exception ex)
            {
                result = "Erro: " + Why(ex);
                log(result); ToFile(result);
                done(result);
            }
        }
    }

    // ------------------------------------------------------------------
    //  Otimizacoes do Windows: limpeza de temporarios (CacheCleaner)
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    //  Drivers direto da fabricante: GPU (NVIDIA / AMD) e chipset (AMD). Intel abre o assistente oficial.
    //  Consulta o site da fabricante, compara com a versao instalada, baixa, confere a assinatura e abre o instalador normal.
    // ------------------------------------------------------------------
    class VItem { public string Title, Inst, Latest, Url, Referer, Pub, Page; }
    class VGpu { public string Name, Drv, Ven; }

    static class VendorDrivers
    {
        const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
        const string IntelPage = "https://www.intel.com/content/www/us/en/support/detect.html";
        const string NvApi = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx";

        static HttpWebRequest Req(string url, string referer)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768;
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.UserAgent = UA; rq.Timeout = 30000; rq.ReadWriteTimeout = 60000; rq.AllowAutoRedirect = true;
            rq.Accept = "text/html,application/xml,application/json,*/*";
            rq.Headers["Accept-Language"] = "en-US,en;q=0.9";
            rq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (referer != null) rq.Referer = referer;
            return rq;
        }

        static string Http(string url, string referer)
        {
            using (WebResponse rs = Req(url, referer).GetResponse())
            using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
        }

        static int[] Ver(string s)
        {
            List<int> l = new List<int>();
            foreach (Match m in Regex.Matches(s ?? "", "\\d+")) { int v; if (int.TryParse(m.Value, out v)) l.Add(v); }
            return l.ToArray();
        }

        static int Cmp(string a, string b)
        {
            int[] x = Ver(a), y = Ver(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p < q ? -1 : 1;
            }
            return 0;
        }

        static string Norm(string s)
        {
            s = (s ?? "").ToLowerInvariant().Replace("(tm)", "").Replace("(r)", "").Replace("nvidia ", "");
            return Regex.Replace(s, "\\s+", " ").Trim();
        }

        static bool Win11()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int b; if (k != null && int.TryParse(Convert.ToString(k.GetValue("CurrentBuild")), out b)) return b >= 22000;
                }
            }
            catch (Exception) { }
            return false;
        }

        // versao de um programa instalado (lista de desinstalacao do Windows)
        static string InstalledVer(string nameContains)
        {
            string[] paths = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };
            foreach (string p in paths)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(p))
                    {
                        if (k == null) continue;
                        foreach (string sn in k.GetSubKeyNames())
                            using (RegistryKey s = k.OpenSubKey(sn))
                            {
                                if (s == null) continue;
                                string dn = s.GetValue("DisplayName") as string;
                                if (dn != null && dn.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    string v = s.GetValue("DisplayVersion") as string;
                                    if (!string.IsNullOrEmpty(v)) return v;
                                }
                            }
                    }
                }
                catch (Exception) { }
            }
            return null;
        }

        static List<VGpu> Gpus()
        {
            List<VGpu> l = new List<VGpu>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name, DriverVersion, PNPDeviceID FROM Win32_VideoController"))
                    foreach (ManagementObject m in s.Get())
                    {
                        string pnp = Convert.ToString(m["PNPDeviceID"]);
                        Match v = Regex.Match(pnp ?? "", "VEN_([0-9A-Fa-f]{4})");
                        if (!v.Success) continue;
                        VGpu g = new VGpu(); g.Name = Convert.ToString(m["Name"]); g.Drv = Convert.ToString(m["DriverVersion"]); g.Ven = v.Groups[1].Value.ToUpperInvariant();
                        l.Add(g);
                    }
            }
            catch (Exception) { }
            return l;
        }

        static string CpuVendor()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_Processor"))
                    foreach (ManagementObject m in s.Get()) return Convert.ToString(m["Manufacturer"]) ?? "";
            }
            catch (Exception) { }
            return "";
        }

        static bool HasBattery()
        {
            try { using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM Win32_Battery")) foreach (ManagementObject m in s.Get()) return true; }
            catch (Exception) { }
            return false;
        }

        // ---------------- NVIDIA ----------------
        static string NvVer(string drv)
        {
            string d = (drv ?? "").Replace(".", "");
            if (d.Length < 5) return null;
            d = d.Substring(d.Length - 5);
            return d.Substring(0, 3) + "." + d.Substring(3);
        }

        static List<string[]> NvList(int type, string parent)
        {
            List<string[]> l = new List<string[]>();
            string x = Http(NvApi + "?TypeID=" + type + "&ParentID=" + parent, null);
            foreach (Match m in Regex.Matches(x, "<Name>(.*?)</Name>\\s*<Value>(\\d+)</Value>", RegexOptions.Singleline))
                l.Add(new string[] { WebUtility.HtmlDecode(m.Groups[1].Value), m.Groups[2].Value });
            return l;
        }

        static bool NvIds(string gpu, out string ps, out string pf)
        {
            ps = pf = null;
            string want = Norm(gpu);
            foreach (string[] ser in NvList(2, "1"))
                foreach (string[] prod in NvList(3, ser[1]))
                    if (Norm(prod[0]) == want) { ps = ser[1]; pf = prod[1]; return true; }
            return false;
        }

        static VItem Nvidia(VGpu g, Action<string> log)
        {
            VItem it = new VItem(); it.Title = "Driver de video NVIDIA (" + g.Name + ")"; it.Pub = "NVIDIA"; it.Page = "https://www.nvidia.com/Download/index.aspx";
            it.Inst = NvVer(g.Drv);
            string ps, pf;
            if (!NvIds(g.Name, out ps, out pf)) { log("NVIDIA: não encontrei '" + g.Name + "' na lista de produtos do site da NVIDIA."); return it; }
            string j = Http("https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid=" + ps + "&pfid=" + pf +
                "&osID=" + (Win11() ? "135" : "57") + "&languageCode=1033&beta=0&isWHQL=0&dltype=-1&dch=1&upCRD=0&qnf=0&sort1=0&numberOfResults=1", null);
            Match v = Regex.Match(j, "\"Version\"\\s*:\\s*\"([0-9.]+)\"");
            Match u = Regex.Match(j, "\"DownloadURL\"\\s*:\\s*\"([^\"]+)\"");
            if (!v.Success || !u.Success) { log("NVIDIA: o site não devolveu nenhum driver para esta placa."); return it; }
            it.Latest = v.Groups[1].Value;
            it.Url = u.Groups[1].Value.Replace("\\/", "/");
            if (it.Url.StartsWith("http:", StringComparison.OrdinalIgnoreCase)) it.Url = "https:" + it.Url.Substring(5);
            return it;
        }

        // ---------------- AMD ----------------
        // pega o maior numero de versao entre os links de instalador encontrados na pagina
        static void AmdPick(string html, string pattern, VItem it, string preferWin)
        {
            string bestV = null, bestU = null;
            foreach (Match m in Regex.Matches(html, pattern, RegexOptions.IgnoreCase))
            {
                string v = m.Groups[1].Value, u = m.Value;
                int c = bestV == null ? 1 : Cmp(v, bestV);
                if (c > 0 || (c == 0 && preferWin != null && u.IndexOf(preferWin, StringComparison.OrdinalIgnoreCase) >= 0 && bestU.IndexOf(preferWin, StringComparison.OrdinalIgnoreCase) < 0))
                { bestV = v; bestU = u; }
            }
            if (bestV != null) { it.Latest = bestV; it.Url = bestU; }
        }

        static VItem AmdGpu(VGpu g, Action<string> log)
        {
            VItem it = new VItem(); it.Title = "Driver de video AMD Radeon (" + g.Name + ")"; it.Pub = "Advanced Micro Devices"; it.Referer = "https://www.amd.com/";
            it.Page = "https://www.amd.com/en/support/download/drivers.html";
            string v = InstalledVer("AMD Software");
            if (v != null && !Regex.IsMatch(v, "^\\d+\\.\\d+\\.\\d+$")) v = null;
            it.Inst = v;
            Match m = Regex.Match(g.Name ?? "", "RX\\s*(\\d{3,4})\\s*(XTX|XT|GRE)?", RegexOptions.IgnoreCase);
            if (!m.Success) { log("AMD: não reconheci o modelo '" + g.Name + "' (GPU integrada?). Use o site da AMD."); return it; }
            string num = m.Groups[1].Value, suf = m.Groups[2].Value.ToLowerInvariant();
            string slug = "amd-radeon-rx-" + num + (suf.Length > 0 ? "-" + suf : "");
            string ser = num.Substring(0, 1) + (num.Length == 4 ? "000" : "00");
            string grp = num.Substring(0, num.Length - 2) + "00";
            string[] pages = {
                "https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-" + ser + "-series/" + slug + ".html",
                "https://www.amd.com/en/support/graphics/amd-radeon-rx-" + ser + "-series/amd-radeon-rx-" + grp + "-series/" + slug };
            foreach (string p in pages)
            {
                try
                {
                    string h = Http(p, "https://www.amd.com/");
                    AmdPick(h, "https://drivers\\.amd\\.com/drivers/[A-Za-z0-9.\\-]*?adrenalin-edition-(\\d+\\.\\d+\\.\\d+)[A-Za-z0-9.\\-]*\\.exe", it, Win11() ? "win11" : "win10");
                    if (it.Latest != null) { it.Page = p; break; }
                }
                catch (Exception) { }
            }
            if (it.Latest == null) log("AMD: não consegui ler a página de drivers do modelo '" + g.Name + "'.");
            return it;
        }

        static VItem AmdChipset(Action<string> log)
        {
            VItem it = new VItem(); it.Title = "Driver de chipset AMD"; it.Pub = "Advanced Micro Devices"; it.Referer = "https://www.amd.com/";
            it.Page = "https://www.amd.com/en/support/download/drivers.html";
            it.Inst = InstalledVer("AMD Chipset Software");
            string[] mob = { "https://www.amd.com/en/support/chipsets/socket-fp5-mobile/amd-ryzen-and-athlon-mobile-chipset" };
            string[] desk = {
                "https://www.amd.com/en/support/chipsets/amd-socket-am5/x670", "https://www.amd.com/en/support/downloads/drivers.html/chipsets/am5/x670.html",
                "https://www.amd.com/en/support/chipsets/amd-socket-am4/x570", "https://www.amd.com/en/support/downloads/drivers.html/chipsets/am4/x570.html" };
            List<string> pages = new List<string>();
            if (HasBattery()) { pages.AddRange(mob); pages.AddRange(desk); } else { pages.AddRange(desk); pages.AddRange(mob); }
            foreach (string p in pages)
            {
                try
                {
                    string h = Http(p, "https://www.amd.com/");
                    AmdPick(h, "https://drivers\\.amd\\.com/drivers/amd_chipset_software_([0-9.]+?)\\.exe", it, null);
                    if (it.Latest != null) { it.Page = p; break; }
                }
                catch (Exception) { }
            }
            if (it.Latest == null) log("AMD: não consegui ler a página do driver de chipset.");
            return it;
        }

        // ---------------- download, assinatura e instalacao ----------------
        static string Download(VItem it, Action<string> log)
        {
            string dir = Path.Combine(Path.GetTempPath(), "BunoTunner_Drivers");
            Directory.CreateDirectory(dir);
            string dst = Path.Combine(dir, Path.GetFileName(new Uri(it.Url).AbsolutePath));
            HttpWebRequest rq = Req(it.Url, it.Referer);
            rq.AutomaticDecompression = DecompressionMethods.None; rq.Timeout = 60000;
            long total, got = 0; int lastPct = -10;
            using (WebResponse rs = rq.GetResponse())
            {
                total = rs.ContentLength;
                log("Baixando " + Path.GetFileName(dst) + (total > 0 ? " (" + (total / 1048576) + " MB)..." : "..."));
                using (Stream src = rs.GetResponseStream())
                using (FileStream fs = new FileStream(dst, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[262144]; int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        fs.Write(buf, 0, n); got += n;
                        if (total > 0)
                        {
                            int pct = (int)(got * 100 / total);
                            if (pct >= lastPct + 10) { lastPct = pct; log("  " + pct + "%"); }
                        }
                    }
                }
            }
            if (got < 1048576 || (total > 0 && got != total)) { try { File.Delete(dst); } catch (Exception) { } throw new Exception("download incompleto ou inválido");
            }
            return dst;
        }

        // assinatura digital do instalador (Get-AuthenticodeSignature confere tambem o hash do arquivo)
        static bool Signed(string file, string publisher, Action<string> log)
        {
            int c;
            string o = Proc.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"$s = Get-AuthenticodeSignature -LiteralPath '" + file.Replace("'", "''") +
                "'; $s.Status.ToString() + '|' + $s.SignerCertificate.Subject\"", 600000, out c).Trim();
            string[] p = o.Split(new char[] { '|' }, 2);
            bool ok = p.Length == 2 && p[0] == "Valid" && p[1].IndexOf(publisher, StringComparison.OrdinalIgnoreCase) >= 0;
            log(ok ? "Assinatura digital valida: " + publisher + "." : "Assinatura digital NÃO confere (" + o + ").");
            return ok;
        }

        static void OpenPage(string url)
        {
            try { ProcessStartInfo psi = new ProcessStartInfo(url); psi.UseShellExecute = true; Process.Start(psi); } catch (Exception) { }
        }

        static void Offer(VItem it, bool install, Action<string> log, Func<string, bool> confirm)
        {
            if (it.Latest == null || it.Url == null)
            {
                log(it.Title + ": não foi possível consultar a versão automaticamente.");
                if (install && it.Page != null && confirm("Não consegui consultar a versão mais recente de:\n" + it.Title + "\n\nAbrir a página oficial de downloads no navegador?")) OpenPage(it.Page);
                return;
            }
            bool known = it.Inst != null;
            if (known && Cmp(it.Inst, it.Latest) >= 0) { log(it.Title + ": já está atualizado (" + it.Inst + ")."); return; }
            log(it.Title + ": instalada " + (known ? it.Inst : "(não identificada)") + " -> mais recente " + it.Latest + ".");
            if (!install) return;
            if (!confirm(it.Title + "\n\nVersão instalada: " + (known ? it.Inst : "não identificada") + "\nVersão mais recente no site da fabricante: " + it.Latest +
                "\n\nBaixar o instalador oficial e abrir a instalação normal para você concluir?")) { log("Ignorado."); return; }
            string file = Download(it, log);
            if (!Signed(file, it.Pub, log)) { try { File.Delete(file); } catch (Exception) { } log("Instalador apagado por segurança. Nada foi executado."); return; }
            log("Abrindo o instalador. Conclua a instalação na janela da fabricante...");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file); psi.UseShellExecute = true; psi.Verb = "runas";
                using (Process p = Process.Start(psi)) { if (p != null) p.WaitForExit(); }
                log("Instalador fechado.");
            }
            catch (Exception ex) { log("Não foi possível abrir o instalador: " + ex.Message); }
        }

        public static void Run(bool install, Action<string> log, Func<string, bool> confirm, Action done)
        {
            try
            {
                log("Consultando os sites das fabricantes...");
                List<VItem> items = new List<VItem>();
                bool intel = false, nv = false, amdGpu = false;
                foreach (VGpu g in Gpus())
                {
                    try
                    {
                        if (g.Ven == "10DE" && !nv) { nv = true; items.Add(Nvidia(g, log)); }
                        else if (g.Ven == "1002" && !amdGpu && Regex.IsMatch(g.Name ?? "", "RX", RegexOptions.IgnoreCase)) { amdGpu = true; items.Add(AmdGpu(g, log)); }
                        else if (g.Ven == "8086") intel = true;
                    }
                    catch (Exception ex) { log("Falha ao consultar " + g.Name + ": " + ex.Message); }
                }
                string cpu = CpuVendor();
                if (cpu.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || cpu.IndexOf("Advanced Micro", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    try { items.Add(AmdChipset(log)); } catch (Exception ex) { log("Falha ao consultar o chipset AMD: " + ex.Message); }
                }
                else if (cpu.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) intel = true;
                if (items.Count == 0 && !intel) log("Nenhuma placa de video NVIDIA/AMD ou chipset AMD identificado.");
                foreach (VItem it in items)
                {
                    try { Offer(it, install, log, confirm); }
                    catch (Exception ex) { log(it.Title + ": falhou (" + ex.Message + ")."); }
                }
                if (intel)
                {
                    log("Intel: a Intel não oferece consulta pública confiável de versões; o Assistente de Drivers e Suporte da Intel faz isso.");
                    if (install && confirm("Foi detectado hardware Intel (video e/ou chipset).\n\nA Intel não permite consultar a versão mais recente automaticamente. Abrir a página oficial do Assistente de Drivers e Suporte da Intel, que detecta e instala os drivers?")) OpenPage(IntelPage);
                }
                log("Concluído.");
            }
            catch (Exception ex) { log("Erro: " + ex.Message); }
            finally { done(); }
        }
    }

    class OptPage : Panel
    {
        // cada otimizacao e uma linha padrao: titulo, descricao, botao principal (e opcional de desfazer) e o resultado
        int top;
        Panel box = new Panel();

        public OptPage()
        {
            Dock = DockStyle.Fill;
            box.Dock = DockStyle.Fill; box.AutoScroll = true; Controls.Add(box);
            AddDrivers();
            AddAll();
            Section("Limpeza e manutenção", "Libera espaço no disco.", false);
            Add("Limpar arquivos temporários", "Apaga C:\\Windows\\Temp, Prefetch e a pasta %temp% do usuário (itens em uso são ignorados). Nenhum documento pessoal é tocado.",
                "Limpar", delegate { return Clean(); }, null, null, null, null);
            Add("Hibernação", "Liga ou desliga a hibernação (powercfg /hibernate). Desligada, apaga o hiberfil.dat (libera vários GB no disco) e também desativa a Inicialização Rápida.",
                "Desativar", delegate { return Pc("off", "Hibernação DESATIVADA (hiberfil.sys removido)."); },
                "Ativar", delegate { return Pc("on", "Hibernação ATIVADA."); },
                delegate { object v = Get(true, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled"); return v != null && Convert.ToInt32(v) == 0 ? "Estado: DESATIVADA" : "Estado: ativada"; }, null);
            Section("Desempenho em jogos", "Ajustes que podem melhorar FPS, tremidas e resposta do mouse.", false);
            Add("Aceleração do mouse (Aprimorar precisão do ponteiro)", "Desligada, o ponteiro anda sempre a mesma distância para o mesmo movimento da mão (recomendado para jogos). Aplica na hora, sem reiniciar.",
                "Desativar", delegate { return Mouse(false); },
                "Ativar", delegate { return Mouse(true); },
                delegate { using (RegistryKey k = Root(false).OpenSubKey(@"Control Panel\Mouse")) { object v = k == null ? null : k.GetValue("MouseSpeed"); return v != null && v.ToString() != "0" ? "Estado: ATIVADA" : "Estado: desativada"; } }, null);
            Add("Desativar MPO (Multiplane Overlay)", "Define OverlayTestMode=5 no DWM. Ajuda a resolver tremidas, telas piscando e telas pretas em alguns monitores/placas NVIDIA e AMD. Reinicie o PC para valer.",
                "Desativar MPO", delegate { Sub(true, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5); return "MPO desativado. Reinicie o PC."; },
                "Reativar", delegate { Del(true, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode"); return "MPO restaurado ao padrão. Reinicie o PC."; },
                delegate { object v = Get(true, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode"); return v != null && Convert.ToInt32(v) == 5 ? "Estado: MPO DESATIVADO" : "Estado: MPO padrão (ativo)"; }, null);
            Add("Desativar compressão de memória", "Executa Disable-MMAgent -mc: o Windows deixa de comprimir a RAM (usa um pouco menos de CPU, um pouco mais de RAM). Vale após reiniciar o PC.",
                "Desativar", delegate { return Ps("Disable-MMAgent -mc", "Compressão de memória desativada. Reinicie o Windows para valer."); },
                "Reativar", delegate { return Ps("Enable-MMAgent -mc", "Compressão de memória reativada. Reinicie o Windows para valer."); },
                delegate { int c; string o = Proc.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"(Get-MMAgent).MemoryCompression\"", 20000, out c); return o.Trim() == "True" ? "Estado: compressão ATIVADA" : o.Trim() == "False" ? "Estado: compressão DESATIVADA" : ""; }, null);
            Add("Overlay DLSS (indicador na tela)", "Mostra no canto do jogo um indicador quando o DLSS está em uso (só placas NVIDIA RTX). Aplica na próxima vez que abrir o jogo.",
                "Ativar", delegate { Sub(true, @"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "ShowDlssIndicator", 0x400); return "Overlay do DLSS ativado."; },
                "Desativar", delegate { Sub(true, @"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "ShowDlssIndicator", 0); return "Overlay do DLSS desativado."; },
                delegate { object v = Get(true, @"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "ShowDlssIndicator"); return v != null && Convert.ToInt32(v) == 0x400 ? "Estado: overlay ATIVO" : "Estado: overlay desligado"; }, null);
            const string HvKey = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
            Add("Isolamento de núcleo (Integridade de memória)", "Recurso de segurança do Windows (HVCI) que isola o núcleo do sistema usando virtualização. Desativar pode dar mais FPS em jogos e liberar drivers antigos, mas reduz a proteção. Reinicie o PC.",
                "Desativar", delegate { Sub(true, HvKey, "Enabled", 0); return "Isolamento de núcleo DESATIVADO. Reinicie o PC."; },
                "Ativar", delegate { Sub(true, HvKey, "Enabled", 1); return "Isolamento de núcleo ATIVADO. Reinicie o PC."; },
                delegate { object v = Get(true, HvKey, "Enabled"); return v != null && Convert.ToInt32(v) == 1 ? "Estado: ATIVADO" : "Estado: desativado"; },
                "Desativar o Isolamento de núcleo reduz a proteção contra malware que ataca o núcleo do Windows. Desativar mesmo assim?");
            const string MmKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
            Add("Mitigações do Windows (Spectre / Meltdown)", "Desativa as mitigações de execução especulativa (FeatureSettingsOverride=3) para ganhar desempenho. REDUZ A SEGURANÇA do PC; use só em máquina de jogos sem dados sensíveis. Reinicie o PC.",
                "Desativar", delegate { Sub(true, MmKey, "FeatureSettingsOverride", 3); Sub(true, MmKey, "FeatureSettingsOverrideMask", 3); return "Mitigações DESATIVADAS. Reinicie o PC."; },
                "Padrão do Windows", delegate { Del(true, MmKey, "FeatureSettingsOverride"); Del(true, MmKey, "FeatureSettingsOverrideMask"); Del(true, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization", "MinVmVersionForCpuBasedMitigations"); return "Mitigações restauradas ao padrão do Windows. Reinicie o PC."; },
                delegate { object v = Get(true, MmKey, "FeatureSettingsOverride"); return v != null ? "Estado: personalizado (FeatureSettingsOverride=" + v + ")" : "Estado: padrão do Windows (mitigações ativas)"; },
                "ATENÇÃO: desativar as mitigações de Spectre/Meltdown deixa o PC mais vulnerável a ataques que leem memória (por exemplo, por código malicioso no navegador). O ganho de desempenho costuma ser pequeno em CPUs recentes.\n\nDesativar mesmo assim?");
            Section("Interface do Windows", "Aparência e comportamento do sistema.", false);
            const string ClickKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
            Add("Menu de clique direito do Windows 10", "No Windows 11, traz de volta o menu de clique direito completo e clássico do Windows 10 (sem o \"Mostrar mais opções\"). Vale ao reiniciar o Explorer ou fazer logoff.",
                "Ativar", delegate { using (RegistryKey k = Root(false).CreateSubKey(ClickKey + "\\InprocServer32")) { k.SetValue("", ""); } return "Menu clássico ativado. Reinicie o Explorer ou faça logoff."; },
                "Desfazer", delegate { Root(false).DeleteSubKeyTree(ClickKey, false); return "Menu padrão do Windows 11 restaurado. Reinicie o Explorer ou faça logoff."; },
                delegate { using (RegistryKey k = Root(false).OpenSubKey(ClickKey + "\\InprocServer32")) return k != null ? "Estado: menu clássico ATIVO" : "Estado: menu padrão do Windows 11"; }, null);
            AddPrivacy();
        }


        // ---------- Privacidade e telemetria: ajustes declarativos (registro + servicos + comandos) ----------
        class R
        {
            public bool M; public string Path, Name; public object Val, Orig;   // Val/Orig null = remover o valor
            public R(bool m, string p, string n, object v, object o) { M = m; Path = p; Name = n; Val = v; Orig = o; }
        }
        class Tw
        {
            public string Title, Desc; public R[] Regs = new R[0]; public string[][] Svcs = new string[0][];   // servico: {nome, iniciar ao aplicar, iniciar ao desfazer}
            public Func<bool, string> Extra; public Label St;
        }
        readonly List<Tw> tws = new List<Tw>();
        class Itm { public string Title; public Func<string> Run, Undo; public Label St; }
        readonly List<Itm> itms = new List<Itm>();
        static bool batch;   // durante "Aplicar todos" o Explorer so reinicia uma vez, no fim

        static void SetR(R r, bool apply)
        {
            object v = apply ? r.Val : r.Orig;
            if (v == null) { Del(r.M, r.Path, r.Name); return; }
            using (RegistryKey k = Root(r.M).CreateSubKey(r.Path)) k.SetValue(r.Name, v, v is int ? RegistryValueKind.DWord : RegistryValueKind.String);
        }

        static string TwState(Tw t)
        {
            if (t.Regs.Length == 0) return "";
            int on = 0;
            foreach (R r in t.Regs)
            {
                object cur = Get(r.M, r.Path, r.Name);
                if (r.Val == null ? cur == null : (cur != null && cur.ToString() == r.Val.ToString())) on++;
            }
            return on == t.Regs.Length ? "Estado: APLICADO" : on == 0 ? "Estado: padrão do Windows" : "Estado: parcialmente aplicado";
        }

        static string RunTw(Tw t, bool apply)
        {
            List<string> bad = new List<string>();
            foreach (R r in t.Regs) { try { SetR(r, apply); } catch (Exception ex) { bad.Add(r.Name + ": " + ex.Message); } }
            foreach (string[] sv in t.Svcs)
            {
                try
                {
                    int c; Proc.Run("sc.exe", "config " + sv[0] + " start= " + (apply ? sv[1] : sv[2]), 20000, out c);
                    if (c != 0 && c != 1060) bad.Add("serviço " + sv[0] + " (código " + c + ")");
                    if (apply) { int c2; Proc.Run("sc.exe", "stop " + sv[0], 20000, out c2); }
                }
                catch (Exception ex) { bad.Add(sv[0] + ": " + ex.Message); }
            }
            if (t.Extra != null) { try { string e = t.Extra(apply); if (!string.IsNullOrEmpty(e)) bad.Add(e); } catch (Exception ex) { bad.Add(ex.Message); } }
            string ok = apply ? "Aplicado." : "Revertido ao padrão do Windows.";
            string st = ""; try { st = TwState(t); } catch (Exception) { }
            return bad.Count == 0 ? ok + (st.Length > 0 ? "  [" + st + "]" : "") : "Concluído com avisos (execute como administrador): " + string.Join("; ", bad.ToArray());
        }

        static void RestartExplorer()
        {
            int c; Proc.Run("taskkill.exe", "/f /im explorer.exe", 15000, out c);
            System.Threading.Thread.Sleep(2500);   // o Windows reinicia o Explorer sozinho
            if (Process.GetProcessesByName("explorer").Length == 0) { try { Process.Start("explorer.exe"); } catch (Exception) { } }
        }

        void AddTw(Tw t)
        {
            tws.Add(t);
            t.St = Add(t.Title, t.Desc, "Aplicar", delegate { return RunTw(t, true); }, "Desfazer", delegate { return RunTw(t, false); },
                t.Regs.Length > 0 ? new Func<string>(delegate { return TwState(t); }) : null, null);
        }

        static R[] Brave()
        {
            const string P = @"SOFTWARE\Policies\BraveSoftware\Brave";
            string[] on = { "BraveRewardsDisabled", "BraveWalletDisabled", "BraveVPNDisabled", "BraveNewsDisabled", "BraveTalkDisabled", "TorDisabled" };
            string[] off = { "BraveAIChatEnabled", "BraveStatsPingEnabled", "BraveP3AEnabled", "UrlKeyedAnonymizedDataCollectionEnabled", "SafeBrowsingExtendedReportingEnabled", "MetricsReportingEnabled" };
            List<R> l = new List<R>();
            foreach (string n in on) l.Add(new R(true, P, n, 1, null));
            foreach (string n in off) l.Add(new R(true, P, n, 0, null));
            return l.ToArray();
        }

        void AddPrivacy()
        {
            Section("Privacidade e telemetria", "Corta recomendações, rastreamento, anúncios e coleta de dados da Microsoft.", false);
            AddTw(new Tw { Title = "Desativar recomendações da Microsoft", Desc = "Impede o Windows de instalar apps sugeridos sozinho e de mostrar recomendações e dicas (DisableWindowsConsumerFeatures e SilentInstalledAppsEnabled).",
                Regs = new R[] {
                    new R(true, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, null),
                    new R(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0, 1) } });
            AddTw(new Tw { Title = "Desativar telemetria", Desc = "Desliga os serviços DiagTrack e WerSvc, a coleta de diagnóstico, ID de publicidade, histórico de atividades, envio automático de amostras do Defender e a telemetria do PowerShell 7.",
                Regs = new R[] {
                    new R(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, null),
                    new R(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, null),
                    new R(false, @"SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0, null),
                    new R(false, @"SOFTWARE\Microsoft\Input\TIPC", "Enabled", 0, null),
                    new R(false, @"SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, null),
                    new R(false, @"SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, null),
                    new R(false, @"SOFTWARE\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0, null),
                    new R(false, @"SOFTWARE\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0, null),
                    new R(true, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0, null),
                    new R(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", 0, null),
                    new R(true, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, null),
                    new R(false, @"SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, null),
                    new R(false, @"SOFTWARE\Microsoft\Siuf\Rules", "PeriodInNanoSeconds", null, null) },
                Svcs = new string[][] { new string[] { "diagtrack", "disabled", "auto" }, new string[] { "WerSvc", "disabled", "demand" } },
                Extra = delegate(bool on)
                {
                    string r = Ps("Set-MpPreference -SubmitSamplesConsent " + (on ? "2" : "1"), "");
                    Environment.SetEnvironmentVariable("POWERSHELL_TELEMETRY_OPTOUT", on ? "1" : null, EnvironmentVariableTarget.Machine);
                    return r.Length > 0 ? "Defender: " + r : null;
                } });
            AddTw(new Tw { Title = "Desativar o Recall (IA que salva prints)", Desc = "Políticas DisableAIDataAnalysis=1 (WindowsAI) e AllowRecallEnablement=0 (Recall): impedem a IA do Windows de capturar e analisar o que aparece na tela.",
                Regs = new R[] {
                    new R(true, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null),
                    new R(true, @"SOFTWARE\Policies\Microsoft\Windows\Recall", "AllowRecallEnablement", 0, null) } });
            AddTw(new Tw { Title = "Desativar apps em segundo plano", Desc = "Impede que os apps da Microsoft Store rodem em segundo plano (BackgroundAccessApplications\\GlobalUserDisabled). Alguns apps deixam de notificar com o programa fechado.",
                Regs = new R[] { new R(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, 0) } });
            AddTw(new Tw { Title = "Desativar rastreamento de localização", Desc = "Nega o acesso a localização, desliga o serviço lfsvc, o sensor de localização e a atualização automática de mapas.",
                Regs = new R[] {
                    new R(true, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny", "Allow"),
                    new R(true, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}", "SensorPermissionState", 0, 1),
                    new R(true, @"SYSTEM\Maps", "AutoUpdateEnabled", 0, 1) },
                Svcs = new string[][] { new string[] { "lfsvc", "disabled", "demand" } } });
            AddTw(new Tw { Title = "Desativar pesquisa com Bing e sugestões no Iniciar", Desc = "Pesquisar no menu Iniciar passa a buscar só no PC (sem Bing, sem sugestões da web e sem Cortana). O Explorer e reiniciado para valer: a barra de tarefas pisca por um instante.",
                Regs = new R[] {
                    new R(false, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, null),
                    new R(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, 1),
                    new R(false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent", 0, null) },
                Extra = delegate(bool on) { if (!batch) RestartExplorer(); return null; } });
            AddTw(new Tw { Title = "Desativar recomendações da Microsoft Store na pesquisa", Desc = "Bloqueia o arquivo store.db da Microsoft Store (icacls /deny) para o Iniciar parar de sugerir apps da loja ao pesquisar. Se o arquivo ainda não existir, abra a Store uma vez.",
                Extra = delegate(bool on)
                {
                    string f = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", @"Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalState\store.db");
                    if (!File.Exists(f)) return "store.db não encontrado (abra a Microsoft Store uma vez e tente de novo)";
                    int c; string o = Proc.Run("icacls.exe", "\"" + f + "\" " + (on ? "/deny" : "/grant") + " *S-1-1-0:F", 20000, out c);
                    return c == 0 ? null : "icacls falhou: " + o.Trim();
                } });
            AddTw(new Tw { Title = "Brave - remover recursos extras", Desc = "Políticas do Brave: desliga Rewards, Carteira, VPN, Leo (IA), News, Talk, Tor, estatísticas, P3A e relatórios de métricas. Vale ao reabrir o Brave.",
                Regs = Brave() });
        }

        // logo abaixo dos drivers: executa TODOS os ajustes da pagina (inclusive os de seguranca) com 1 clique
        void AddAll()
        {
            XpGroup g = new XpGroup(); g.Title = "Aplicar todos os ajustes (1 clique)"; g.SetBounds(12, top, 638, 84); top += 94;
            Label d = UI.Lbl(g, "Executa TODOS os ajustes desta página, um após o outro. O overlay do DLSS não vai ser executado.", 12, 22, Xp.Ui, Xp.Dim);
            d.AutoSize = false; d.SetBounds(12, 22, 500, 34);
            Label st = UI.Lbl(g, "", 12, 58, Xp.Ui, Xp.GroupText); st.AutoSize = false; st.SetBounds(12, 58, 500, 18);
            XpButton bAll = UI.Btn(g, "Aplicar todos", 524, 22, 112, null); bAll.Height = 28;
            XpButton bUn = UI.Btn(g, "Desfazer todos", 524, 52, 112, null);
            Action<bool> go = delegate(bool apply)
            {
                string msg = apply
                    ? "Serão executados TODOS os ajustes desta página (menos o overlay do DLSS), incluindo os que REDUZEM A SEGURANÇA: Isolamento de núcleo (HVCI) e mitigações Spectre/Meltdown desativados, hibernação desligada, limpeza de temporários, privacidade/telemetria (serviços, Recall, Brave, pesquisa) e outros. O Explorer será reiniciado no fim e alguns itens pedem reiniciar o PC.\n\nContinuar?"
                    : "Serão desfeitos todos os ajustes desta página que tem opção de desfazer (a limpeza de temporários não tem). Alguns itens pedem reiniciar o PC.\n\nContinuar?";
                if (Dlg.Show(this, msg, "BunoTunner - Otimizações", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                bAll.Enabled = false; bUn.Enabled = false;
                List<Itm> list = new List<Itm>();
                foreach (Itm it in itms) if (!it.Title.StartsWith("Overlay DLSS") && (apply ? it.Run != null : it.Undo != null)) list.Add(it);   // o overlay do DLSS nunca entra
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    batch = true; int n = 0, warn = 0;
                    foreach (Itm t in list)
                    {
                        n++; Itm tt = t; int k = n;
                        try { BeginInvoke(new Action(delegate { st.Text = Lang.T(apply ? "Aplicando" : "Desfazendo") + " " + k + "/" + list.Count + ": " + Lang.T(tt.Title); tt.St.Text = Lang.T("Executando..."); })); } catch (Exception) { }
                        string r; try { r = (apply ? t.Run : t.Undo)(); } catch (Exception ex) { r = "Erro: " + ex.Message; }
                        if (r.StartsWith("Concluído com avisos") || r.StartsWith("Erro") || r.StartsWith("Falhou")) warn++;
                        try { BeginInvoke(new Action(delegate { tt.St.Text = r; })); } catch (Exception) { }
                    }
                    batch = false;
                    if (apply) { try { BeginInvoke(new Action(delegate { st.Text = Lang.T("Reiniciando o Explorer..."); })); } catch (Exception) { } RestartExplorer(); }
                    try { BeginInvoke(new Action(delegate { st.Text = Lang.T(apply ? "Todos aplicados" : "Todos desfeitos") + ": " + (list.Count - warn) + "/" + list.Count + (warn > 0 ? "  (" + warn + " " + Lang.T("com aviso, veja a lista abaixo") + ")" : ""); bAll.Enabled = true; bUn.Enabled = true; })); } catch (Exception) { }
                });
            };
            bAll.Click += delegate { go(true); };
            bUn.Click += delegate { go(false); };
            box.Controls.Add(g);
        }

        // faixa de categoria: separa os grupos de ajustes (a de seguranca usa a cor de alerta)
        void Section(string title, string sub, bool warn)
        {
            top += 6;
            SecBar b = new SecBar(title, sub, warn); b.SetBounds(0, top, 650, 30);
            box.Controls.Add(b); top += 38;
        }

        class SecBar : Control
        {
            string t, sub; bool warn;
            public SecBar(string title, string subtitle, bool w) { t = title; sub = subtitle; warn = w; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics; Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                if (Xp.Theme == 0)
                {
                    // Luna: barra no estilo da barra de titulo do XP (azul vivo, brilho no topo, texto branco)
                    using (LinearGradientBrush br = new LinearGradientBrush(r, Color.FromArgb(78, 130, 236), Color.FromArgb(30, 82, 206), 90f)) g.FillRectangle(br, r);
                    using (Pen hl = new Pen(Color.FromArgb(140, 178, 248))) g.DrawLine(hl, 1, 1, Width - 3, 1);
                    using (Pen p = new Pen(Color.FromArgb(0, 48, 152))) g.DrawRectangle(p, r);
                    TextRenderer.DrawText(g, Lang.T(t), Xp.UiBold, new Rectangle(10, 0, 230, Height), Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, Lang.T(sub), Xp.Ui, new Rectangle(240, 0, Width - 250, Height), Color.FromArgb(222, 234, 255), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    return;
                }
                Color a = warn ? Xp.Bad : Xp.PaneTop, bcol = warn ? Color.FromArgb(Math.Max(0, Xp.Bad.R - 60), Math.Max(0, Xp.Bad.G - 30), Math.Max(0, Xp.Bad.B - 30)) : Xp.PaneBot;
                using (LinearGradientBrush br = new LinearGradientBrush(r, a, bcol, 0f)) g.FillRectangle(br, r);
                using (Pen p = new Pen(Xp.Border)) g.DrawRectangle(p, r);
                Color tc = warn ? Color.White : Xp.PaneText;
                TextRenderer.DrawText(g, Lang.T(t), Xp.UiBold, new Rectangle(10, 0, 230, Height), tc, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, Lang.T(sub), Xp.Ui, new Rectangle(240, 0, Width - 250, Height), tc, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        // primeiro item da lista: atualiza os drivers (Windows Update + site da NVIDIA/AMD)
        void AddDrivers()
        {
            Section("Drivers", "Mantenha a GPU, o chipset e os demais dispositivos atualizados.", false);
            XpGroup g = new XpGroup(); g.Title = "Atualizar drivers (todos os dispositivos, GPU e Chipset)"; g.SetBounds(12, top, 638, 148);
            Label d = UI.Lbl(g, "Analisa o PC e procura drivers mais novos no Windows Update e, para a placa de video (NVIDIA / AMD) e o chipset AMD, direto no site da fabricante. Antes de instalar, cria um ponto de restauração e confere a assinatura digital (e o hash SHA-256 no Windows Update); se algo estiver alterado, nada é instalado. Os instaladores da fabricante são baixados e abertos para você concluir; para Intel, abre o Assistente de Drivers da Intel.", 12, 22, Xp.Ui, Xp.Dim);
            d.AutoSize = false; d.SetBounds(12, 22, 624, 58);
            XpButton bGo = UI.Btn(g, "Atualizar drivers", 12, 86, 150, null);
            XpButton bScan = UI.Btn(g, "Só verificar", 170, 86, 120, null);
            bGo.Height = 24; bScan.Height = 24;
            XpProgress pb = new XpProgress(); pb.SetBounds(300, 91, 336, 14); g.Controls.Add(pb);
            XpOption chkWu = UI.Opt(g, "Windows Update (todos os dispositivos)", 12, 116, 300, false); chkWu.Checked = true;
            XpOption chkVend = UI.Opt(g, "Site da fabricante (GPU NVIDIA/AMD e chipset AMD)", 316, 116, 320, false); chkVend.Checked = true;
            // o andamento aparece num popup (aberto ao clicar em "So verificar" ou "Atualizar drivers")
            TextBox log = null;   // um novo TextBox a cada popup (fechar o popup descarta o anterior)
            XpForm pop = null; bool busy = false;
            Action showPop = delegate
            {
                if (pop == null || pop.IsDisposed)
                {
                    log = new TextBox(); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.WordWrap = true;
                    log.Font = Xp.Ui; log.BackColor = Xp.EditBg; log.ForeColor = Xp.Text; log.Dock = DockStyle.Fill;
                    pop = new XpForm(); pop.Text = Lang.T("Atualizar drivers - andamento");
                    pop.FormClosing += delegate(object so, FormClosingEventArgs fe)
                    {
                        if (busy && fe.CloseReason == CloseReason.UserClosing &&
                            Dlg.Show(pop, "ATENÇÃO: o processo ainda não terminou.\n\nFechar esta janela NÃO interrompe a operação: ela continua em segundo plano. Não desligue nem reinicie o PC e não feche o BunoTunner até o fim: interromper a instalação de um driver pode deixar o Windows ou a placa de vídeo instáveis.\n\nFechar a janela mesmo assim?", "BunoTunner - Aviso de segurança", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                            fe.Cancel = true;
                    }; pop.Size = new Size(660, 440); pop.StartPosition = FormStartPosition.CenterParent;
                    Panel bot = new Panel(); bot.Dock = DockStyle.Bottom; bot.Height = 36; bot.BackColor = Color.Transparent;
                    XpButton bClose = UI.Btn(bot, "Fechar", 0, 6, 100, delegate { pop.Close(); }); bClose.Anchor = AnchorStyles.Top | AnchorStyles.Right; bClose.Left = 640 - 8 - 100 - 8;
                    pop.Controls.Add(log); pop.Controls.Add(bot); log.BringToFront();
                }
                if (!pop.Visible) pop.Show(FindForm());
                pop.Activate();
            };
            Timer anim = new Timer(); anim.Interval = 60; anim.Tick += delegate { pb.Step(); };
            Action<bool> start = delegate(bool install)
            {
                bool wu = chkWu.Checked, vend = chkVend.Checked;
                if (!wu && !vend) { showPop(); log.Text = Lang.T("Marque pelo menos uma das origens (Windows Update ou site da fabricante)."); return; }
                busy = true; bGo.Enabled = false; bScan.Enabled = false; pb.Marquee = true; anim.Start(); showPop(); log.Text = "";
                Action<string> add = delegate(string s) { try { BeginInvoke(new Action(delegate { if (log != null && !log.IsDisposed) log.AppendText(Lang.T(s) + "\r\n"); })); } catch (Exception) { } };
                Action finish = delegate
                {
                    try { BeginInvoke(new Action(delegate { busy = false; anim.Stop(); pb.Marquee = false; pb.Value = 0; pb.Invalidate(); bGo.Enabled = true; bScan.Enabled = true; })); } catch (Exception) { }
                };
                Func<string, bool> askVendor = delegate(string msg)
                {
                    bool yes = false;
                    try { Invoke(new Action(delegate { yes = Dlg.Show(this, msg, "BunoTunner - Drivers", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; })); }
                    catch (Exception) { }
                    return yes;
                };
                Action runVendor = delegate
                {
                    if (wu) add("---- Site da fabricante ----");
                    VendorDrivers.Run(install, add, askVendor, finish);
                };
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    if (!wu) { runVendor(); return; }
                    add("---- Windows Update ----");
                    DriverUpdater.Run(install, add,
                        delegate(string lst)
                        {
                            bool yes = false;
                            try
                            {
                                Invoke(new Action(delegate
                                {
                                    yes = Dlg.Show(this, "Estes drivers serão baixados, verificados (assinatura e hash SHA-256) e instalados:\n\n" + lst +
                                        "\nSerá criado um ponto de restauração antes. A tela pode piscar (driver de video) e pode ser preciso reiniciar o Windows.\n\nContinuar?",
                                        "BunoTunner - Otimizações", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
                                }));
                            }
                            catch (Exception) { }
                            return yes;
                        },
                        delegate(string r)
                        {
                            if (vend) runVendor(); else finish();
                        });
                });
            };
            bGo.Click += delegate { start(true); };
            bScan.Click += delegate { start(false); };
            box.Controls.Add(g);
            top += 158;
        }

        static RegistryKey Root(bool machine) { return RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64); }
        static void Sub(bool machine, string path, string name, int val) { using (RegistryKey k = Root(machine).CreateSubKey(path)) k.SetValue(name, val, RegistryValueKind.DWord); }
        static void Del(bool machine, string path, string name) { using (RegistryKey k = Root(machine).OpenSubKey(path, true)) if (k != null) k.DeleteValue(name, false); }
        static object Get(bool machine, string path, string name) { using (RegistryKey k = Root(machine).OpenSubKey(path)) return k == null ? null : k.GetValue(name); }

        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint action, uint param, int[] vparam, uint winIni);

        static string Pc(string onOff, string ok)
        {
            int c; string o = Proc.Run("powercfg.exe", "/hibernate " + onOff, 30000, out c);
            return c == 0 ? ok : "Falhou (execute como administrador): " + o;
        }

        // MouseSpeed/Threshold = aceleracao do Windows; SPI_SETMOUSE aplica na hora
        static string Mouse(bool on)
        {
            string sp = on ? "1" : "0", t1 = on ? "6" : "0", t2 = on ? "10" : "0";
            using (RegistryKey k = Root(false).CreateSubKey(@"Control Panel\Mouse"))
            {
                k.SetValue("MouseSpeed", sp, RegistryValueKind.String);
                k.SetValue("MouseThreshold1", t1, RegistryValueKind.String);
                k.SetValue("MouseThreshold2", t2, RegistryValueKind.String);
            }
            SystemParametersInfo(0x0004, 0, new int[] { int.Parse(t1), int.Parse(t2), int.Parse(sp) }, 3);
            return on ? "Aceleração do mouse ATIVADA." : "Aceleração do mouse DESATIVADA.";
        }

        static string Ps(string cmd, string ok)
        {
            int c;
            string o = Proc.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"" + cmd + "\"", 60000, out c);
            return c == 0 ? ok : "Falhou (execute como administrador): " + o;
        }

        static string Clean()
        {
            string tmp = Environment.GetEnvironmentVariable("TEMP") ?? "";
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] dirs = { Path.Combine(win, "Temp"), Path.Combine(win, "Prefetch"), tmp };
            long freed = 0; int files = 0;
            foreach (string d in dirs)
            {
                if (d.Length < 8 || !Directory.Exists(d)) continue;
                try
                {
                    foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                        try { long sz = new FileInfo(f).Length; File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); freed += sz; files++; } catch (Exception) { }
                    string[] subs = Directory.GetDirectories(d, "*", SearchOption.AllDirectories);
                    Array.Sort(subs, delegate(string x, string y) { return y.Length.CompareTo(x.Length); });
                    foreach (string sd in subs) try { Directory.Delete(sd); } catch (Exception) { }
                }
                catch (Exception) { }
            }
            return "Concluído: " + files + " arquivo(s) apagado(s), " + (freed / 1048576.0).ToString("0.0") + " MB liberados.";
        }

        Label Add(string title, string desc, string runText, Func<string> run, string undoText, Func<string> undo, Func<string> state, string confirm)
        {
            XpGroup g = new XpGroup(); g.Title = title; g.SetBounds(12, top, 638, 78); top += 88;
            Label d = UI.Lbl(g, desc, 12, 22, Xp.Ui, Xp.Dim);
            d.AutoSize = false; d.SetBounds(12, 22, 500, 32);
            Label st = UI.Lbl(g, "", 12, 56, Xp.Ui, Xp.GroupText);
            st.AutoSize = false; st.SetBounds(12, 56, 500, 18);
            if (state != null) System.Threading.ThreadPool.QueueUserWorkItem(delegate { string t; try { t = state(); } catch (Exception) { t = ""; } try { BeginInvoke(new Action(delegate { if (st.Text.Length == 0) st.Text = t; })); } catch (Exception) { } });
            XpButton b1 = UI.Btn(g, runText, 524, 22, 112, null);
            XpButton b2 = undoText != null ? UI.Btn(g, undoText, 524, 50, 112, null) : null;
            b1.Height = 24;
            Action<Func<string>> go = delegate(Func<string> f)
            {
                b1.Enabled = false; if (b2 != null) b2.Enabled = false; st.Text = "Executando...";
                Func<string> fin = f;
                if (state != null) f = delegate { string r0 = fin(); string t = ""; try { t = state(); } catch (Exception) { } return t.Length > 0 ? r0 + "  [" + t + "]" : r0; };
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string r;
                    try { r = f(); } catch (Exception ex) { r = "Erro: " + ex.Message; }
                    try { BeginInvoke(new Action(delegate { st.Text = r; b1.Enabled = true; if (b2 != null) b2.Enabled = true; })); } catch (Exception) { }
                });
            };
            b1.Click += delegate { if (confirm != null && Dlg.Show(confirm, "BunoTunner - Otimizações", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return; go(run); };
            if (b2 != null) b2.Click += delegate { go(undo); };
            box.Controls.Add(g);
            itms.Add(new Itm { Title = title, Run = run, Undo = undo, St = st });
            return st;
        }
    }

    class LatGraph : Control
    {
        public List<double> Kb = new List<double>(), Ns = new List<double>();
        public LatGraph() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Xp.GraphBg);
            Rectangle r = new Rectangle(34, 6, Width - 42, Height - 22);
            using (Pen gp = new Pen(Xp.GraphGrid)) { for (int i = 0; i <= 4; i++) g.DrawLine(gp, r.X, r.Y + r.Height * i / 4, r.Right, r.Y + r.Height * i / 4); }
            double mx = 10; foreach (double v in Ns) if (v > mx) mx = v;
            mx = Math.Ceiling(mx / 10) * 10;
            using (Font f = new Font(Xp.Ui.FontFamily, 7f))
            {
                for (int i = 0; i <= 4; i++) TextRenderer.DrawText(g, (mx * (4 - i) / 4).ToString("0"), f, new Point(2, r.Y + r.Height * i / 4 - 6), Xp.GraphText);
                string[] lab = { "4K", "32K", "256K", "2M", "16M", "128M" }; int[] idx = { 0, 3, 6, 9, 12, 15 };
                for (int i = 0; i < lab.Length; i++) TextRenderer.DrawText(g, lab[i], f, new Point(r.X + r.Width * idx[i] / 16 - 8, r.Bottom + 1), Xp.GraphText);
                if (Ns.Count == 0) TextRenderer.DrawText(g, Lang.T("ns x tamanho do bloco"), f, new Point(r.X + 8, r.Y + 4), Xp.GraphText);
            }
            if (Ns.Count > 1)
            {
                PointF[] pts = new PointF[Ns.Count];
                for (int i = 0; i < Ns.Count; i++) pts[i] = new PointF(r.X + r.Width * i / 16f, r.Bottom - (float)(Ns[i] / mx) * r.Height);
                using (Pen lp = new Pen(Color.FromArgb(40, 190, 90), 2f)) g.DrawLines(lp, pts);
                foreach (PointF p in pts) g.FillEllipse(Brushes.Orange, p.X - 2.5f, p.Y - 2.5f, 5, 5);
            }
            using (Pen bp = new Pen(Xp.GraphGrid)) g.DrawRectangle(bp, r);
        }
    }

    // lista padrao do programa: botao direito / Ctrl+C copia so as linhas selecionadas; opcionalmente (AutoFit) a altura acompanha o numero de linhas
    class XpList : ListView
    {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RCT r);
        [StructLayout(LayoutKind.Sequential)] struct RCT { public int L, T, R, B; }
        public bool AutoFit, SelfLayout;   // SelfLayout: quem usa a lista reposiciona o resto (evento Fitted)
        public event EventHandler Fitted;
        int maxH = -1; bool fitting, pending;
        ContextMenuStrip own;

        public XpList()
        {
            own = new ContextMenuStrip(); own.RenderMode = ToolStripRenderMode.System;
            AddCopyItem(own);
            ContextMenuStrip = own;
            ContextMenuStripChanged += delegate { if (ContextMenuStrip != null && ContextMenuStrip != own) AddCopyItem(ContextMenuStrip); };
        }

        void AddCopyItem(ContextMenuStrip m)
        {
            foreach (ToolStripItem x in m.Items) if (x.Tag is string && (string)x.Tag == "xpcopy") return;
            if (m.Items.Count > 0) { ToolStripSeparator sp = new ToolStripSeparator(); sp.Tag = "xpcopy"; m.Items.Add(sp); }
            ToolStripItem it = m.Items.Add("Copiar linha selecionada", null, delegate { CopySelected(); });
            it.Tag = "xpcopy";
            m.Opening += delegate { it.Enabled = SelectedItems.Count > 0; };
            Lang.Menu(m);
        }

        public void CopySelected()
        {
            if (SelectedItems.Count == 0) return;
            StringBuilder sb = new StringBuilder();
            foreach (ListViewItem it in SelectedItems)
            {
                if (sb.Length > 0) sb.Append("\r\n");
                for (int i = 0; i < it.SubItems.Count; i++) { if (i > 0) sb.Append('\t'); sb.Append(it.SubItems[i].Text); }
            }
            try { Clipboard.SetText(sb.ToString()); } catch (Exception) { }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                ListViewHitTestInfo h = HitTest(e.Location);
                if (h.Item != null && !h.Item.Selected) { SelectedItems.Clear(); h.Item.Selected = true; h.Item.Focused = true; }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.C && !e.Alt) { CopySelected(); e.Handled = true; e.SuppressKeyPress = true; }
        }

        // ---- altura = numero de linhas ----
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); QueueFit(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) QueueFit(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (!fitting && AutoFit && IsHandleCreated) { if (Height > maxH) maxH = Height; QueueFit(); } }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // LVM_INSERTITEMA/W, LVM_DELETEITEM, LVM_DELETEALLITEMS
            if (AutoFit && (m.Msg == 0x1007 || m.Msg == 0x104D || m.Msg == 0x1008 || m.Msg == 0x1009)) QueueFit();
        }
        void QueueFit()
        {
            if (!AutoFit || pending || !IsHandleCreated) return;
            pending = true;
            try { BeginInvoke(new Action(delegate { pending = false; FitRows(); })); } catch (Exception) { pending = false; }
        }

        int HeaderH()
        {
            if (HeaderStyle == ColumnHeaderStyle.None) return 0;
            try { IntPtr hh = SendMessage(Handle, 0x101F, IntPtr.Zero, IntPtr.Zero); RCT r; if (hh != IntPtr.Zero && GetWindowRect(hh, out r) && r.B > r.T) return r.B - r.T; } catch (Exception) { }
            return Font.Height + 8;
        }

        public void FitRows()
        {
            if (!AutoFit || Dock != DockStyle.None || !IsHandleCreated || Parent == null || View != View.Details || Groups.Count > 0) return;
            if (Height > maxH) maxH = Height;   // o limite so cresce: nunca fica preso num tamanho pequeno
            if (Items.Count == 0) return;       // lista vazia (ex.: sendo recarregada): mantem a altura atual
            int rowH = Items.Count > 0 ? Items[0].GetBounds(ItemBoundsPortion.Entire).Height : Font.Height + 4;
            if (rowH < 10) rowH = Font.Height + 4;
            int colW = 0; foreach (ColumnHeader c in Columns) colW += c.Width;
            int extra = colW > ClientSize.Width ? SystemInformation.HorizontalScrollBarHeight : 0;
            int chrome = Height - ClientSize.Height;
            int need = HeaderH() + Math.Max(1, Items.Count) * rowH + chrome + extra + 2;
            int minH = HeaderH() + rowH + chrome + 2;
            int nh = Math.Max(minH, Math.Min(maxH, need));
            if (nh == Height) return;
            int delta = nh - Height, oldBottom = Bottom;
            fitting = true;
            try { Height = nh; if (SelfLayout) { if (Fitted != null) Fitted(this, EventArgs.Empty); } else Reflow(this, oldBottom, delta); }
            finally { fitting = false; }
        }

        // sobe/desce o que esta abaixo (e cresce/encolhe o grupo que contem a lista) para nao sobrar espaco vazio
        static void Reflow(Control c, int oldBottom, int delta)
        {
            Control p = c.Parent; if (p == null) return;
            p.SuspendLayout();
            List<KeyValuePair<Control, AnchorStyles>> saved = new List<KeyValuePair<Control, AnchorStyles>>();
            foreach (Control k in p.Controls)
                if (k.Dock == DockStyle.None) { saved.Add(new KeyValuePair<Control, AnchorStyles>(k, k.Anchor)); k.Anchor = AnchorStyles.Top | AnchorStyles.Left; }
            foreach (Control k in p.Controls)
                if (k != c && k.Dock == DockStyle.None && k.Top >= oldBottom - 1 && k.Left < c.Right && k.Right > c.Left) k.Top += delta;
            XpGroup g = p as XpGroup; int pOldBottom = p.Bottom;
            if (g != null) g.Height += delta;
            foreach (KeyValuePair<Control, AnchorStyles> kv in saved) kv.Key.Anchor = kv.Value;
            p.ResumeLayout(true);
            if (g != null) Reflow(p, pOldBottom, delta);
        }
    }

    static class UI
    {
        public static Label Lbl(Control p, string t, int x, int y, Font f = null, Color? c = null)
        {
            Label l = new Label();
            l.AutoSize = true; l.BackColor = Color.Transparent; l.Text = t;
            l.Font = f ?? Xp.Ui; l.ForeColor = c ?? Xp.Text; l.Location = new Point(x, y);
            p.Controls.Add(l);
            return l;
        }
        public static XpButton Btn(Control p, string t, int x, int y, int w, EventHandler h)
        {
            XpButton b = new XpButton(); b.Text = t; b.SetBounds(x, y, w, 24);
            if (h != null) b.Click += h;
            p.Controls.Add(b);
            return b;
        }
        public static XpText Txt(Control p, int x, int y, int w, string t)
        {
            XpText b = new XpText(); b.Location = new Point(x, y); b.Width = w; b.Text = t;
            p.Controls.Add(b);
            return b;
        }
        public static XpOption Opt(Control p, string t, int x, int y, int w, bool radio)
        {
            XpOption o = new XpOption(); o.Text = t; o.Radio = radio; o.SetBounds(x, y, w, 18);
            p.Controls.Add(o);
            return o;
        }
        public static void Bind(XpSlider s, XpText t)
        {
            s.ValueChanged += delegate { string v = s.Value.ToString(); if (t.Text != v) t.Text = v; };
            EventHandler commit = delegate { int v; if (int.TryParse(t.Text.Trim(), out v)) s.Value = v; t.Text = s.Value.ToString(); };
            t.Box.Leave += commit;
            t.Box.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; commit(o, EventArgs.Empty); }
            };
        }
        public static int Int(XpText t, int def) { int v; return int.TryParse(t.Text.Trim(), out v) ? v : def; }
        public static void DoubleBuffer(Control c)
        {
            try { typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true, null); } catch (Exception) { }
        }
    }

    // ------------------------------------------------------------------
    //  Janela principal
    // ------------------------------------------------------------------
    class MainForm : XpForm
    {
        ComboBox cmb = new ComboBox();
        Gpu cur;
        Panel pgFan, pgOc, pgSpec, pgCpu, pgBench, pgRam, pgAff, pgUnin, pgNet, pgSto, pgOpt;
        FanPage fanPage; RamPage ramPage; AffinityPage affPage;
        XpIconButton btnRam, btnAff, btnUnin, btnNet, btnSto, btnOpt;
        Label lblGpuSel;
        ListView lvCpu = new XpList();
        List<ListViewItem> cpuLive = new List<ListViewItem>();
        Timer timer = new Timer();
        Label status = new Label();
        string statusText;
        SensorForm sensForm;
        XpIconButton btnFan, btnOc, btnSpec, btnCpu, btnBench;
        LTip tip = new LTip();
        // benchmark
        ComboBox cbBThr, cbBRam, cbBDrive, cbBSize, cbTheme;
        Label lblTheme, lblBRate, lblBAvg, lblBPeak, lblBTemp, lblBErr, lblBTime, lblBMsg;
        XpOption chkBSafe; XpText txBTemp; XpProgress pbBench;
        XpGraph grBench = new XpGraph(); ListView lvBench = new XpList(); Timer tmBench = new Timer();
        Sn bsnRate, bsnTemp; float? bRateNow, bTempNow;
        RenderForm rf; bool bExt; string bU = ""; double bSc = 1; Action bFallback; bool bRunning; int bKind, bDurSec, bN, gpuGen; long bLast; DateTime bStart, bLastT; double bSum, bPeak, bTempMax;
        static readonly string[] BName = { "GPU", "CPU", "FPU", "RAM", "SSD" };
        static readonly string[] BUnit = { "GFLOPS", "Mrays/s", "GFLOPS", "GB/s", "MB/s" };
        static readonly double[] BScale = { 1e-9, 1e-6, 1e-9, 1e-9, 1e-6 };
        const string PayPalUrl = "https://www.paypal.com/donate/?business=LYD3FYNZCDRLE&no_recurring=0&item_name=Thanks%21&currency_code=BRL&source=qr";
        const string LivePixUrl = "https://livepix.gg/buno";

        // overclock
        XpSlider slCore, slMem, slPow, slTemp;
        XpText txCore, txMem, txPow, txTemp, txLockC1, txLockC2, txLockM1, txLockM2;
        Label rgCore, rgMem, rgPow, rgTemp, lblOcMsg, lblClocks, lblUCore, lblUMem, lblUPow, lblUTemp, lblOcInfo;
        XpGroup grpOc; XpButton btnOcApply, btnOcReset;
        List<Control> ocLock = new List<Control>();
        // specs
        ListView lvSpec = new XpList();

        public MainForm()
        {
            Text = "BunoTunner";
            Size = new Size(772, 706);

            Startup.Mark("MainForm: início");
            SensorHub.Open();
            Startup.Mark("SensorHub.Open concluído");

            tip.InitialDelay = 200; tip.ReshowDelay = 100; tip.AutoPopDelay = 8000;

            Panel right = new Panel(); right.Dock = DockStyle.Fill; right.Padding = new Padding(8);
            TaskPane left = new TaskPane(); left.Dock = DockStyle.Left; left.Width = 72;
            Panel bar = new Panel(); bar.Dock = DockStyle.Bottom; bar.Height = 30; bar.BackColor = Xp.Face;
            status.AutoSize = false; status.BackColor = Color.Transparent; status.TextAlign = ContentAlignment.MiddleLeft;
            statusText = SensorHub.Status(); status.SetBounds(6, 7, 250, 16); status.Text = statusText;
            bar.Controls.Add(status);

            // doacoes (canto inferior direito)
            XpIconButton bPay = new XpIconButton("paypal", 24), bLive = new XpIconButton("livepix", 24);
            bPay.Top = 3; bLive.Top = 3;
            bar.Controls.Add(bPay); bar.Controls.Add(bLive);
            EventHandler place = delegate { bLive.Left = bar.Width - 32; bPay.Left = bar.Width - 62; };
            bar.Resize += place; place(null, EventArgs.Empty);
            bPay.Click += delegate { Donate(PayPalUrl, "PayPal"); };
            bLive.Click += delegate { Donate(LivePixUrl, "LivePix"); };
            tip.SetToolTip(bPay, "Apoie o BunoTunner via PayPal");
            tip.SetToolTip(bLive, "Apoie o BunoTunner via LivePix");

            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(bar);

            // painel lateral: so icones (a descricao aparece ao passar o mouse)
            btnFan = IconBtn(left, "fan", 12, 262, "Ventoinhas\nAutomático, manual (% e RPM) e curva de temperatura", delegate { ShowPage(pgFan); });
            btnOc = IconBtn(left, "oc", 12, 312, "Overclock\nCore clock, memory clock, TDP, limite de temperatura e travar clocks", delegate { ShowPage(pgOc); });
            btnSpec = IconBtn(left, "gpu", 12, 62, "Especificações da GPU\nTodos os dados da placa de video", delegate { ShowPage(pgSpec); });
            btnCpu = IconBtn(left, "chip", 12, 12, "Especificações da CPU\nProcessador, núcleos, cache, clocks e conjuntos de instruções", delegate { ShowPage(pgCpu); });
            btnBench = IconBtn(left, "bench", 12, 362, "Benchmark / Estresse\nGPU, CPU, FPU, RAM e SSD com verificação de erros e limite de temperatura", delegate { ShowPage(pgBench); });
            btnRam = IconBtn(left, "ram", 12, 112, "Memória RAM\nVelocidade, canais, voltagem, módulos e timings básicos (tCAS, tRCD, tRP, tRAS, tRC, tRFC, Command Rate)", delegate { ShowPage(pgRam); });
            btnSto = IconBtn(left, "health", 12, 162, "Saúde do armazenamento\nVida útil restante, temperatura e S.M.A.R.T. de SSDs NVMe/SATA e HDs", delegate { ShowPage(pgSto); });
            btnNet = IconBtn(left, "net", 12, 212, "Internet\nTeste de velocidade (ping, download, upload) e informações da rede", delegate { ShowPage(pgNet); });
            btnAff = IconBtn(left, "aff", 12, 412, "Afinidade de processos\nEscolha em quais núcleos cada programa roda; presets e regras automáticas", delegate { ShowPage(pgAff); });
            btnUnin = IconBtn(left, "trash", 12, 462, "Desinstalador\nRemove programas por completo: pastas, registro, inicialização, serviços e tarefas que sobram; também remove o que se recusa a desinstalar", delegate { ShowPage(pgUnin); });
            btnOpt = IconBtn(left, "opt", 12, 512, "Otimizações do Windows\nLimpeza de temporários, compressão de memória e outros ajustes", delegate { ShowPage(pgOpt); });
            IconBtn(left, "chart", 12, 562, "Sensores\nTudo em uma janela: GPU, CPU, placa-mae, memória, discos e ventoinhas, com gráfico, busca e alertas", delegate { OpenSensors(); });

            // topo: escolha da GPU
            lblGpuSel = UI.Lbl(right, "Placa de video:", 0, 6, Xp.UiBold);
            cmb.DropDownStyle = ComboBoxStyle.DropDownList; cmb.Font = Xp.Ui;
            cmb.SetBounds(95, 2, 330, 22);
            right.Controls.Add(cmb);
            // PawnIO (ao lado da escolha da GPU)
            XpButton bPawn = new XpButton(); bPawn.Text = PawnIo.Installed() ? "PawnIO instalado" : "Instalar PawnIO";
            bPawn.SetBounds(440, 2, 150, 22); bPawn.Enabled = !PawnIo.Installed();
            bPawn.Click += delegate { PawnIo.InstallNow(this); };
            right.Controls.Add(bPawn);
            tip.SetToolTip(bPawn, "PawnIO: driver usado pelo LibreHardwareMonitor para ler sensores da CPU/placa-mae e controlar as ventoinhas da placa-mae. Clique para baixar e instalar.");
            // iniciar com o Windows (canto superior direito, mesmo tamanho dos botoes PayPal/LivePix)
            bWin = new XpIconButton("win10", 24); bWin.Top = 2;
            right.Controls.Add(bWin); bWin.BringToFront();
            right.Resize += delegate { bWin.Left = right.ClientSize.Width - 32; };
            bWin.Left = 676 - 24;
            SetAutoBtn(false);
            bWin.Click += delegate { ToggleAutoStart(); };
            // botao de atualizacao (a esquerda do "iniciar com o Windows")
            bUpd = new XpIconButton("update", 24); bUpd.Top = 2;
            right.Controls.Add(bUpd); bUpd.BringToFront();
            right.Resize += delegate { bUpd.Left = right.ClientSize.Width - 62; };
            bUpd.Left = 676 - 54;
            tip.SetToolTip(bUpd, "Verificar atualizações\nProcura uma versão mais nova do BunoTunner e, se você quiser, baixa e instala.");
            bUpd.Click += delegate { CheckUpdate(); };
            lblTheme = UI.Lbl(bar, "Tema:", 264, 8, Xp.UiBold);
            cbTheme = new ComboBox(); cbTheme.DropDownStyle = ComboBoxStyle.DropDownList; cbTheme.Font = Xp.Ui; cbTheme.SetBounds(306, 4, 176, 22); cbTheme.DropDownWidth = 230;
            cbTheme.Items.AddRange(Xp.ThemeNames);
            cbTheme.SelectedIndex = Xp.Theme;
            bar.Controls.Add(cbTheme);
            UI.Lbl(bar, "Idioma:", 492, 8, Xp.UiBold);
            ComboBox cbLang = new ComboBox(); cbLang.DropDownStyle = ComboBoxStyle.DropDownList; cbLang.Font = Xp.Ui; cbLang.SetBounds(544, 4, 140, 22); cbLang.DropDownWidth = 170;
            cbLang.Items.AddRange(new object[] { "Auto (Windows)", "Portugu\u00EAs (Brasil)", "English" });
            string lsv = Settings.Get("lang", "auto");
            cbLang.SelectedIndex = lsv == "pt" ? 1 : lsv == "en" ? 2 : 0;
            bar.Controls.Add(cbLang);
            cbLang.Tag = "nolang"; cbTheme.Tag = "nolang";
            cbLang.SelectedIndexChanged += delegate
            {
                Settings.Set("lang", cbLang.SelectedIndex == 1 ? "pt" : cbLang.SelectedIndex == 2 ? "en" : "auto");
                Lang.Init();
                Lang.Reapply();
            };
            cbTheme.SelectedIndexChanged += delegate
            {
                Xp.SetTheme(cbTheme.SelectedIndex);
                Settings.Set("theme", Xp.ThemeIds[cbTheme.SelectedIndex]);
                Xp.ApplyAll();
            };
            tip.SetToolTip(cbTheme, "Muda o visual do programa: Luna (XP), Frutiger Aero (Win7), Dark/AMOLED, Neon, Steampunk ou Vintage (Windows 95)");
            foreach (Gpu g in SensorHub.Gpus) cmb.Items.Add(g);

            Panel host = new Panel(); host.SetBounds(0, 34, 676, 586);
            host.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            right.Controls.Add(host);
            fanPage = new FanPage(); pgFan = fanPage; pgOc = BuildOcPage(); pgSpec = BuildSpecPage(); pgCpu = BuildCpuPage(); pgBench = BuildBenchPage();
            ramPage = new RamPage(); pgRam = ramPage; affPage = new AffinityPage(); pgAff = affPage; pgUnin = new UninstallPage(); pgNet = new NetPage(); pgSto = new StoragePage(); pgOpt = new OptPage();
            host.Controls.Add(pgFan); host.Controls.Add(pgOc); host.Controls.Add(pgSpec); host.Controls.Add(pgCpu); host.Controls.Add(pgBench); host.Controls.Add(pgRam); host.Controls.Add(pgAff); host.Controls.Add(pgUnin); host.Controls.Add(pgNet); host.Controls.Add(pgSto); host.Controls.Add(pgOpt);
            Shown += delegate
            {
                Startup.Mark("janela mostrada");
                Lang.Apply(this, tip);
                Lang.StartSweeper();
                Startup.CloseSplash();
                Startup.Save();
                RefreshAutoBtn();
                Overlay.Init();
                try { File.Delete(Application.ExecutablePath + ".old"); } catch (Exception) { }
                bool trayStart = false;
                foreach (string a in Environment.GetCommandLineArgs()) if (a == "--tray") trayStart = true;
                if (trayStart && tray != null && Settings.Get("tray", "1") == "1") Hide(); else ForceFront();
                StartGpuExtras();
                ramPage.LoadAsync();
                // WMI e lento: coleta a CPU em segundo plano para nao travar a janela
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    List<string[]> rows = CpuInfo.Collect();
                    try { BeginInvoke(new Action(delegate { FillCpu(rows); })); } catch (Exception) { }
                });
            };
            Startup.Mark("MainForm: páginas criadas");
            ShowPage(pgCpu);

            cmb.SelectedIndexChanged += delegate { LoadGpu(); };
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
            else
            {
                Dlg.Show("Nenhuma placa de video foi detectada.\nRode como administrador e confira a pasta libs.", "BunoTunner");
            }

            timer.Interval = 1000;
            timer.Tick += delegate { try { Tick(); } catch (Exception) { } };
            timer.Start();
            FormClosing += OnClosing;
            InitTray();
        }

        XpIconButton IconBtn(Control parent, string kind, int x, int y, string tipText, EventHandler h)
        {
            XpIconButton b = new XpIconButton(kind, 48);
            b.Location = new Point(x, y);
            b.Click += h;
            parent.Controls.Add(b);
            tip.SetToolTip(b, tipText);
            return b;
        }

        void Donate(string url, string nome)
        {
            if (string.IsNullOrEmpty(url)) { Dlg.Show("O link do " + nome + " ainda não foi configurado.\nEm breve!", "BunoTunner"); return; }
            try { Process.Start(url); } catch (Exception ex) { Dlg.Show(ex.Message, "BunoTunner"); }
        }

        void ShowPage(Panel p)
        {
            Panel[] pages = { pgFan, pgOc, pgSpec, pgCpu, pgBench, pgRam, pgAff, pgUnin, pgNet, pgSto, pgOpt };
            XpIconButton[] btns = { btnFan, btnOc, btnSpec, btnCpu, btnBench, btnRam, btnAff, btnUnin, btnNet, btnSto, btnOpt };
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = p == pages[i];
                if (btns[i] != null) { btns[i].Selected = p == pages[i]; btns[i].Invalidate(); }
            }
            lblGpuSel.Visible = cmb.Visible = p != pgCpu && p != pgRam && p != pgAff && p != pgUnin && p != pgNet && p != pgSto && p != pgOpt;
        }

        void OpenSensors()
        {
            if (sensForm == null || sensForm.IsDisposed)
            {
                List<Sn> all = new List<Sn>();
                all.AddRange(SensorHub.GpuSensors); all.AddRange(SensorHub.PcSensors);
                sensForm = new SensorForm("BunoTunner - Sensores (GPU, CPU, placa-mae, memória, discos, ventoinhas...)", all);
                sensForm.Show();
            }
            else { if (sensForm.WindowState == FormWindowState.Minimized) sensForm.WindowState = FormWindowState.Normal; sensForm.BringToFront(); }
        }

        void OcRow(Control parent, string title, int y, XpSlider s, XpText t, out Label unit, out Label range)
        {
            UI.Lbl(parent, title, 14, y, Xp.UiBold);
            s.SetBounds(14, y + 14, 400, 34); parent.Controls.Add(s);
            t.Location = new Point(424, y + 20); t.Width = 56; parent.Controls.Add(t);
            unit = UI.Lbl(parent, "MHz", 486, y + 24);
            range = UI.Lbl(parent, "", 530, y + 24, Xp.Ui, Xp.Dim);
            UI.Bind(s, t);
        }

        Panel BuildOcPage()
        {
            Panel p = new Panel(); p.Dock = DockStyle.Fill;
            XpGroup g = new XpGroup(); g.Title = "Overclock / limites"; g.SetBounds(0, 0, 676, 436);
            grpOc = g;
            Label warn = new Label();
            warn.SetBounds(14, 22, 648, 34); warn.Tag = "warn"; warn.BackColor = Xp.EditBg; warn.BorderStyle = BorderStyle.FixedSingle;
            warn.ForeColor = Xp.Text;
            warn.Text = "  AVISO: overclock pode causar travamentos e artefatos. Vá aos poucos e teste a estabilidade.\n  Vale até reiniciar ou clicar em Redefinir. Funciona em NVIDIA, AMD (Overdrive8) e Intel Arc.";
            warn.Font = Xp.Ui; g.Controls.Add(warn);

            slCore = new XpSlider(); txCore = new XpText();
            slMem = new XpSlider(); txMem = new XpText();
            slPow = new XpSlider(); txPow = new XpText();
            slTemp = new XpSlider(); txTemp = new XpText();
            OcRow(g, "Core Clock (offset)", 66, slCore, txCore, out lblUCore, out rgCore);
            OcRow(g, "Memory Clock (offset)", 118, slMem, txMem, out lblUMem, out rgMem);
            OcRow(g, "Power Limit / TDP", 170, slPow, txPow, out lblUPow, out rgPow);
            OcRow(g, "Temp Limit (alvo térmico)", 222, slTemp, txTemp, out lblUTemp, out rgTemp);

            ocLock.Clear();
            ocLock.Add(UI.Lbl(g, "Travar clock do núcleo (MHz):  min", 14, 282));
            txLockC1 = UI.Txt(g, 210, 278, 50, ""); ocLock.Add(txLockC1); ocLock.Add(UI.Lbl(g, "max", 268, 282)); txLockC2 = UI.Txt(g, 292, 278, 50, ""); ocLock.Add(txLockC2);
            ocLock.Add(UI.Btn(g, "Travar", 352, 276, 70, delegate { LockClocks(true); }));
            ocLock.Add(UI.Lbl(g, "Travar clock da memória (MHz):  min", 14, 312));
            txLockM1 = UI.Txt(g, 210, 308, 50, ""); ocLock.Add(txLockM1); ocLock.Add(UI.Lbl(g, "max", 268, 312)); txLockM2 = UI.Txt(g, 292, 308, 50, ""); ocLock.Add(txLockM2);
            ocLock.Add(UI.Btn(g, "Travar", 352, 306, 70, delegate { LockClocks(false); }));
            ocLock.Add(UI.Btn(g, "Destravar tudo", 432, 291, 110, delegate { UnlockClocks(); }));

            btnOcApply = UI.Btn(g, "Aplicar", 14, 352, 120, delegate { ApplyOc(); }); btnOcApply.Height = 28;
            btnOcReset = UI.Btn(g, "Redefinir tudo", 144, 352, 120, delegate { ResetOc(); }); btnOcReset.Height = 28;
            lblOcMsg = UI.Lbl(g, "", 14, 386, Xp.UiBold, Xp.Good);
            lblOcMsg.AutoSize = false; lblOcMsg.SetBounds(14, 386, 648, 46);
            p.Controls.Add(g);

            XpGroup c = new XpGroup(); c.Title = "Clocks atuais e suporte desta placa"; c.SetBounds(0, 446, 676, 134);
            lblClocks = UI.Lbl(c, "-", 14, 20);
            lblOcInfo = UI.Lbl(c, "", 14, 42, Xp.Ui, Xp.Dim);
            lblOcInfo.AutoSize = false; lblOcInfo.SetBounds(14, 42, 648, 62);
            UI.Lbl(c, "Crossbar / Uncore / Fabric clock: nenhuma API pública (NVML, NvAPI, ADL, IGCL) expõe esses clocks, então nenhum programa de terceiros consegue alterá-los.", 14, 100, Xp.Ui, Xp.Dim)
                .MaximumSize = new Size(648, 0);
            p.Controls.Add(c);
            return p;
        }

        void LockClocks(bool core)
        {
            if (cur == null || !cur.Nv) return;
            int mn = UI.Int(core ? txLockC1 : txLockM1, 0), mx = UI.Int(core ? txLockC2 : txLockM2, 0);
            if (mn <= 0 || mx < mn) { lblOcMsg.Text = "Informe min e max válidos."; return; }
            int r = core ? Nvml.LockGpu(cur.H, (uint)mn, (uint)mx) : Nvml.LockMem(cur.H, (uint)mn, (uint)mx);
            lblOcMsg.Text = r == 0 ? "Clock travado." : "Falhou: " + Nvml.Err(r);
        }

        void UnlockClocks()
        {
            if (cur == null || !cur.Nv) return;
            int a = Nvml.UnlockGpu(cur.H), b = Nvml.UnlockMem(cur.H);
            lblOcMsg.Text = (a == 0 || b == 0) ? "Clocks destravados." : "Falhou: " + Nvml.Err(a);
        }

        // Configura um slider a partir da capacidade lida do driver
        void OcSetup(XpSlider s, XpText t, Label rg, Label unit, OcCap c, string tipText)
        {
            s.Enabled = t.Enabled = c.Ok;
            unit.Text = c.Unit.Length > 0 ? c.Unit : unit.Text;
            if (c.Ok)
            {
                s.Minimum = c.Min; s.Maximum = c.Max;
                s.Value = Math.Max(c.Min, Math.Min(c.Max, c.Cur));
                t.Text = s.Value.ToString();
                rg.Text = "(" + c.Min + Lang.Pick(" a ", " to ") + c.Max + ")";
            }
            else
            {
                s.Minimum = 0; s.Maximum = 1; s.Value = 0; t.Text = "-";
                rg.Text = "(indisponível)";
            }
            tip.SetToolTip(s, c.Ok ? tipText : "Indisponível: " + c.Why);
        }

        void ApplyOc()
        {
            GpuOc oc = cur == null ? null : cur.Oc;
            if (oc == null) { lblOcMsg.ForeColor = Xp.Bad; lblOcMsg.Text = "Overclock indisponível para esta GPU. " + (cur != null ? cur.OcWhy : ""); return; }
            List<string> lines = new List<string>();
            bool bad = false;
            Action<string, string> add = delegate(string k, string r) { lines.Add(k + ": " + r); if (!GpuOc.IsOk(r) && !r.StartsWith("não suportado")) bad = true; };
            if (oc.Core.Ok) { add("Core", oc.SetCore(slCore.Value)); }
            if (oc.Mem.Ok) { add("Mem", oc.SetMem(slMem.Value)); }
            if (oc.Power.Ok && slPow.Value != oc.Power.Cur) { string r = oc.SetPower(slPow.Value); if (GpuOc.IsOk(r)) oc.Power.Cur = slPow.Value; add("TDP", r); }
            if (oc.Temp.Ok && slTemp.Value != oc.Temp.Cur) { string r = oc.SetTemp(slTemp.Value); if (GpuOc.IsOk(r)) oc.Temp.Cur = slTemp.Value; add("Temp", r); }
            if (lines.Count == 0) lines.Add("Nenhum ajuste disponível nesta placa.");
            lblOcMsg.ForeColor = bad ? Xp.Bad : Xp.Good;
            lblOcMsg.Text = string.Join("\n", lines.ToArray());
        }

        void ResetOc()
        {
            GpuOc oc = cur == null ? null : cur.Oc;
            if (oc == null) return;
            string r = oc.ResetAll();
            LoadOc();
            lblOcMsg.ForeColor = GpuOc.IsOk(r) ? Xp.Good : Xp.Bad;
            lblOcMsg.Text = GpuOc.IsOk(r) ? "Valores originais restaurados." : "Redefinir: " + r;
        }

        // le do driver o que a GPU atual permite e prepara a pagina de OC
        void LoadOc()
        {
            GpuOc oc = cur == null ? null : cur.Oc;
            bool any = oc != null;
            foreach (Control c in ocLock) c.Enabled = any && oc.CanLock && cur.Nv;
            btnOcApply.Enabled = btnOcReset.Enabled = any;
            if (any)
            {
                oc.Refresh();
                grpOc.Title = Lang.T("Overclock / limites - ") + oc.Backend;
                OcSetup(slCore, txCore, rgCore, lblUCore, oc.Core, "Offset do clock do núcleo, em MHz acima do padrão");
                OcSetup(slMem, txMem, rgMem, lblUMem, oc.Mem, "Offset do clock da memória, em MHz");
                OcSetup(slPow, txPow, rgPow, lblUPow, oc.Power, "Limite de potência (TDP)");
                OcSetup(slTemp, txTemp, rgTemp, lblUTemp, oc.Temp, "Limite de temperatura");
                List<string> info = new List<string>();
                if (!oc.Core.Ok) info.Add("Core: " + oc.Core.Why);
                else if (oc.Core.Why.Length > 0) info.Add("Core: " + oc.Core.Why);
                if (!oc.Mem.Ok) info.Add("Memória: " + oc.Mem.Why);
                else if (oc.Mem.Why.Length > 0) info.Add("Memória: " + oc.Mem.Why);
                if (!oc.Power.Ok) info.Add("TDP: " + oc.Power.Why);
                if (!oc.Temp.Ok) info.Add("Temperatura: " + oc.Temp.Why);
                if (oc.Note.Length > 0 && (!oc.Core.Ok || !oc.Mem.Ok)) info.Add(oc.Note);
                lblOcInfo.Text = info.Count == 0 ? "Esta placa expõe todos os ajustes acima. " + oc.Note : string.Join("\n", info.ToArray());
                lblOcMsg.Text = "";
            }
            else
            {
                grpOc.Title = Lang.T("Overclock / limites");
                XpSlider[] ss = { slCore, slMem, slPow, slTemp }; XpText[] tt = { txCore, txMem, txPow, txTemp }; Label[] rr = { rgCore, rgMem, rgPow, rgTemp };
                for (int i = 0; i < 4; i++) { ss[i].Enabled = tt[i].Enabled = false; ss[i].Minimum = 0; ss[i].Maximum = 1; ss[i].Value = 0; tt[i].Text = "-"; rr[i].Text = "(indisponível)"; }
                string why = cur == null ? "" : cur.OcWhy;
                lblOcInfo.Text = "Overclock indisponível para esta GPU. " + why;
                lblOcMsg.ForeColor = Xp.Bad;
                lblOcMsg.Text = "Nenhum backend de overclock para esta GPU. " + why;
            }
        }

        // ---------------- pagina de especificacoes ----------------
        Panel BuildSpecPage()
        {
            Panel p = new Panel(); p.Dock = DockStyle.Fill;
            XpGroup g = new XpGroup(); g.Title = "Especificações da GPU selecionada"; g.Dock = DockStyle.Fill; g.Padding = new Padding(10, 22, 10, 40);
            lvSpec.Dock = DockStyle.Fill; lvSpec.View = View.Details; lvSpec.FullRowSelect = true; lvSpec.GridLines = true;
            lvSpec.BackColor = Xp.EditBg; lvSpec.Font = Xp.Ui;
            lvSpec.Columns.Add("Propriedade", 250); lvSpec.Columns.Add("Valor", 380);
            g.Controls.Add(lvSpec);
            XpButton copy = new XpButton(); copy.Text = "Copiar tudo"; copy.SetBounds(10, 0, 110, 24);
            copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            g.Controls.Add(copy);
            XpButton look = new XpButton(); look.Text = "LookUp"; look.SetBounds(126, 0, 110, 24);
            look.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            g.Controls.Add(look);
            g.Resize += delegate { copy.Top = g.Height - 32; look.Top = copy.Top; };
            look.Click += delegate { if (cur == null) { Dlg.Show(this, "Nenhuma GPU selecionada.", "BunoTunner"); return; } Tpu.Open(cur.Name, true, this); };
            copy.Click += delegate
            {
                StringBuilder sb = new StringBuilder();
                foreach (ListViewItem it in lvSpec.Items) sb.AppendLine(it.Text + ": " + it.SubItems[1].Text);
                try { Clipboard.SetText(sb.ToString()); } catch (Exception) { }
            };
            p.Controls.Add(g);
            return p;
        }

        // ---------------- helpers de lista / combos ----------------
        static void AddSpec(ListView lv, string[] r)
        {
            ListViewItem it = new ListViewItem(r);
            string v = r.Length > 1 ? r[1] : "";
            if (r[0].StartsWith("---")) it.ForeColor = Xp.GroupText;
            else if (v.StartsWith("Ligado")) it.ForeColor = Xp.Good;
            else if (v.StartsWith("Desligado")) it.ForeColor = Xp.Bad;
            else if (v.StartsWith("Indisponível")) it.ForeColor = Xp.Dim;
            lv.Items.Add(it);
        }

        ComboBox Combo(Control parent, int x, int y, int w, params string[] items)
        {
            ComboBox c = new ComboBox(); c.DropDownStyle = ComboBoxStyle.DropDownList; c.Font = Xp.Ui; c.SetBounds(x, y, w, 22);
            foreach (string s in items) c.Items.Add(s);
            if (items.Length > 0) c.SelectedIndex = 0;
            parent.Controls.Add(c);
            return c;
        }

        void StartGpuExtras()
        {
            Gpu g = cur;
            if (g == null) return;
            int gen = gpuGen;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<string[]> rows = GpuWmi.Collect(g.Name);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (gen != gpuGen) return;
                        foreach (string[] r in rows) AddSpec(lvSpec, r);
                    }));
                }
                catch (Exception) { }
            });
        }

        // ---------------- pagina de benchmark / estresse ----------------
        XpIconButton[] bBtn = new XpIconButton[5];
        ComboBox[] cbDurK = new ComboBox[5]; ComboBox cbBFur; static readonly int[] FurSteps = { 50000, 100000, 250000, 500000, 1000000 };
        ComboBox cbBThrF;
        XpButton btnBSeq;

        // um cartao por teste (grade de 2 colunas): icone que inicia/para, descricao, duracao e opcoes do teste
        XpGroup BenchBlock(int k, string title, string desc, string icon, string tipText)
        {
            XpGroup g = new XpGroup(); g.Title = title; g.SetBounds(0, k * 74, 676, 70);
            XpIconButton b = new XpIconButton(icon, 48); b.Location = new Point(12, 19); b.Badge = 1;
            int kk = k;
            b.Click += delegate { ToggleBench(kk); };
            tip.SetToolTip(b, tipText);
            g.Controls.Add(b); bBtn[k] = b;
            Label d = UI.Lbl(g, desc, 72, 20, Xp.Ui, Xp.Dim);
            d.AutoSize = false; d.SetBounds(72, 20, 300, 46);
            UI.Lbl(g, "Duração:", 384, 24);
            cbDurK[k] = Combo(g, 448, 20, 110, "30 s", "1 min", "5 min", "10 min", "Até parar"); cbDurK[k].SelectedIndex = 1;
            return g;
        }

        Panel BuildBenchPage()
        {
            Panel p = new Panel(); p.Dock = DockStyle.Fill;

            XpGroup gG = BenchBlock(0, "GPU  -  render 3D (estilo FurMark)", "Janela OpenGL com bola de pelos e shader; mostra o FPS. Fechar a janela para o teste.", "gpu", "Iniciar / parar o estresse de GPU");
            UI.Lbl(gG, "Pelos:", 384, 48);
            cbBFur = Combo(gG, 448, 44, 110, "50 mil", "100 mil", "250 mil", "500 mil", "1 milhão"); cbBFur.SelectedIndex = 2;
            tip.SetToolTip(cbBFur, "Quantidade de pelos do render. Mais pelos = teste mais pesado (FPS menor).");
            UI.Lbl(gG, "Placa:", 72, 54);
            lblBGpu = UI.Lbl(gG, cur != null ? cur.Name : "-", 112, 54, Xp.UiBold, Xp.GroupText);
            lblBGpu.AutoSize = false; lblBGpu.SetBounds(112, 54, 260, 14);
            tip.SetToolTip(lblBGpu, "A placa e a escolhida la em cima. Se o OpenGL abrir em outra, o teste muda para OpenCL na escolhida.");
            p.Controls.Add(gG);

            XpGroup gC = BenchBlock(1, "CPU  -  ray tracing 3D (estilo Cinebench)", "Renderiza uma cena em todos os núcleos, bloco a bloco. Pontos = Mrays/s x 100.", "chip", "Iniciar / parar o estresse de CPU");
            UI.Lbl(gC, "Threads:", 384, 48);
            cbBThr = Combo(gC, 448, 44, 110, "Todas", "Metade", "1");
            p.Controls.Add(gC);

            XpGroup gF = BenchBlock(2, "FPU  -  campo de ondas em ponto flutuante", "Cálculo de ondas com seno/cosseno em todos os núcleos; mostra GFLOPS estimado.", "fpu", "Iniciar / parar o estresse de FPU");
            UI.Lbl(gF, "Threads:", 384, 48);
            cbBThrF = Combo(gF, 448, 44, 110, "Todas", "Metade", "1");
            cbBThr.SelectedIndexChanged += delegate { if (cbBThrF.SelectedIndex != cbBThr.SelectedIndex) cbBThrF.SelectedIndex = cbBThr.SelectedIndex; };
            cbBThrF.SelectedIndexChanged += delegate { if (cbBThr.SelectedIndex != cbBThrF.SelectedIndex) cbBThr.SelectedIndex = cbBThrF.SelectedIndex; };
            p.Controls.Add(gF);

            XpGroup gR = BenchBlock(3, "RAM  -  escrita e leitura com verificação", "Grava e le padrões na memória e confere cada valor; qualquer diferença conta como erro.", "ram", "Iniciar / parar o estresse de RAM");
            UI.Lbl(gR, "Usar (% da livre):", 384, 48);
            cbBRam = Combo(gR, 490, 44, 70, "25%", "50%", "70%"); cbBRam.SelectedIndex = 1;
            p.Controls.Add(gR);

            XpGroup gS = BenchBlock(4, "SSD  -  sequencial + 4K aleatório", "Usa um arquivo temporário no drive escolhido (apagado ao fim). Em 'Até parar' gera desgaste.", "ssd", "Iniciar / parar o estresse de SSD");
            UI.Lbl(gS, "Drive:", 384, 48);
            cbBDrive = Combo(gS, 426, 44, 62);
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try { if (d.DriveType == DriveType.Fixed && d.IsReady) cbBDrive.Items.Add(d.Name); } catch (Exception) { }
            }
            if (cbBDrive.Items.Count > 0)
            {
                int si = cbBDrive.Items.IndexOf(Path.GetPathRoot(Environment.SystemDirectory));
                cbBDrive.SelectedIndex = si >= 0 ? si : 0;
            }
            UI.Lbl(gS, "Tamanho:", 496, 48);
            cbBSize = Combo(gS, 556, 44, 90, "512 MB", "1 GB", "2 GB", "4 GB"); cbBSize.SelectedIndex = 1;
            p.Controls.Add(gS);

            // cartao de controle: seguranca, progresso, sequencia e mensagens
            XpGroup gK = new XpGroup(); gK.Title = "Controle, segurança e resultado ao vivo"; gK.SetBounds(0, 376, 676, 208);
            chkBSafe = UI.Opt(gK, "Parar se passar de", 12, 28, 150, false); chkBSafe.Checked = true;
            txBTemp = UI.Txt(gK, 168, 25, 40, "95");
            UI.Lbl(gK, "\u00B0C", 212, 29);
            pbBench = new XpProgress(); pbBench.SetBounds(12, 60, 316, 18);
            gK.Controls.Add(pbBench);
            btnBSeq = UI.Btn(gK, "Rodar sequência (CPU, FPU, RAM, GPU)", 12, 88, 316, delegate { ToggleSeq(); });
            btnBSeq.Height = 28;
            tip.SetToolTip(btnBSeq, "Roda CPU, FPU, RAM e GPU um após o outro, cada um com a duração do seu bloco (o SSD fica de fora para não gerar desgaste).");
            p.Controls.Add(gK);
            BenchRestore();

            XpGroup g3 = new XpGroup(); g3.Title = "Resultado ao vivo"; g3.SetBounds(0, 380, 676, 204);
            UI.Lbl(g3, "Taxa atual", 14, 22, Xp.UiBold);
            lblBRate = UI.Lbl(g3, "-", 14, 36, Xp.Big, Xp.GroupText);
            string[] caps = { "Media:", "Pico:", "Temperatura:", "Erros:", "Tempo:" };
            Label[] vals = new Label[5], capL = new Label[5];
            for (int i = 0; i < 5; i++)
            {
                capL[i] = UI.Lbl(g3, caps[i], 14, 68 + i * 15, Xp.UiBold);
                vals[i] = UI.Lbl(g3, "-", 100, 68 + i * 15);
            }
            lblBAvg = vals[0]; lblBPeak = vals[1]; lblBTemp = vals[2]; lblBErr = vals[3]; lblBTime = vals[4];
            lblBMsg = UI.Lbl(g3, "Clique no ícone de um teste para iniciar.", 14, 146, Xp.Ui, Xp.Dim);
            lblBMsg.AutoSize = false; lblBMsg.SetBounds(14, 146, 232, 54);
            grBench.SetBounds(252, 22, 412, 100); grBench.Window = 120;
            g3.Controls.Add(grBench);
            lvBench.SetBounds(252, 126, 412, 50); lvBench.View = View.Details; lvBench.FullRowSelect = true; lvBench.GridLines = true;
            lvBench.BackColor = Xp.EditBg; lvBench.Font = Xp.Ui;
            lvBench.Columns.Add("Teste", 38); lvBench.Columns.Add("Dur.", 38); lvBench.Columns.Add("Media", 82); lvBench.Columns.Add("Pico", 82);
            lvBench.Columns.Add("Erros", 36); lvBench.Columns.Add("T.max", 40); lvBench.Columns.Add("Estab.", 46); lvBench.Columns.Add("vs ant.", 50);
            g3.Controls.Add(lvBench);
            XpButton bCopy = UI.Btn(g3, "Copiar", 252, 178, 72, delegate { BenchCopy(); });
            XpButton bCsv = UI.Btn(g3, "Salvar CSV", 330, 178, 84, delegate { BenchCsv(); });
            XpButton bClr = UI.Btn(g3, "Limpar", 420, 178, 64, delegate { lvBench.Items.Clear(); });
            bCopy.Height = bCsv.Height = bClr.Height = 22;
            // "Resultado ao vivo" fica fora da tela (os controles continuam existindo); so a linha de status aparece, no cartao de controle
            lblBMsg.Parent = gK; lblBMsg.SetBounds(12, 124, 316, 72);
            // taxa e estatisticas do teste em andamento ficam visiveis no cartao de controle
            UI.Lbl(gK, "Taxa atual", 352, 26, Xp.UiBold);
            lblBRate.Parent = gK; lblBRate.AutoSize = false; lblBRate.SetBounds(352, 42, 310, 30);
            for (int i = 0; i < 5; i++)
            {
                capL[i].Parent = gK; capL[i].SetBounds(352, 84 + i * 21, 100, 18);
                vals[i].Parent = gK; vals[i].AutoSize = false; vals[i].SetBounds(456, 84 + i * 21, 206, 18);
            }

            tmBench.Interval = 1000;
            tmBench.Tick += delegate { try { BenchTick(); } catch (Exception) { } };
            return p;
        }

        void ToggleSeq()
        {
            if (bRunning) { StopBench("Interrompido pelo usuário."); return; }
            bQueue = new Queue<int>(new int[] { 1, 2, 3, 0 }); bSeqRun = true;
            ToggleBench(-1);
        }

        bool bGlChecked; Label lblBGpu;
        double bSq; Queue<int> bQueue = new Queue<int>(); bool bSeqRun;
        void BenchRestore()
        {
            try
            {
                int fv;
                if (int.TryParse(Settings.Get("benchFur", "2"), out fv) && fv >= 0 && fv < cbBFur.Items.Count) cbBFur.SelectedIndex = fv;
                string st = Settings.Get("bench", "");
                string[] a = st.Split(',');
                if (a.Length < 7) return;
                int v;
                if (int.TryParse(a[1], out v) && v >= 0 && v < 5) foreach (ComboBox cd in cbDurK) cd.SelectedIndex = v;
                if (int.TryParse(a[2], out v) && v >= 0 && v < cbBThr.Items.Count) cbBThr.SelectedIndex = v;
                if (int.TryParse(a[3], out v) && v >= 0 && v < cbBRam.Items.Count) cbBRam.SelectedIndex = v;
                if (int.TryParse(a[4], out v) && v >= 0 && v < cbBSize.Items.Count) cbBSize.SelectedIndex = v;
                chkBSafe.Checked = a[5] == "1";
                if (a[6].Length > 0 && a[6].Length < 4) txBTemp.Box.Text = a[6];
            }
            catch (Exception) { }
        }
        void BenchSave(int kind)
        {
            try
            {
                Settings.Set("bench", string.Join(",", new string[] { kind.ToString(), cbDurK[kind].SelectedIndex.ToString(), cbBThr.SelectedIndex.ToString(), cbBRam.SelectedIndex.ToString(), cbBSize.SelectedIndex.ToString(), chkBSafe.Checked ? "1" : "0", txBTemp.Box.Text }));
            }
            catch (Exception) { }
        }
        string BenchText(string sep)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Join(sep, new string[] { "Teste", "Duração", "Media", "Pico", "Erros", "Temp max", "Estabilidade", "Variação" }));
            for (int i = lvBench.Items.Count - 1; i >= 0; i--)
            {
                List<string> c = new List<string>();
                foreach (ListViewItem.ListViewSubItem x in lvBench.Items[i].SubItems) c.Add(x.Text);
                sb.AppendLine(string.Join(sep, c.ToArray()));
            }
            return sb.ToString();
        }
        void BenchCopy()
        {
            if (lvBench.Items.Count == 0) { lblBMsg.Text = "Nenhum resultado para copiar ainda."; return; }
            try { Clipboard.SetText(BenchText("\t")); lblBMsg.Text = "Resultados copiados."; } catch (Exception) { }
        }
        void BenchCsv()
        {
            if (lvBench.Items.Count == 0) { lblBMsg.Text = "Nenhum resultado para salvar ainda."; return; }
            using (SaveFileDialog sd = new SaveFileDialog())
            {
                sd.Filter = "CSV|*.csv"; sd.FileName = "benchmark_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv";
                if (sd.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(sd.FileName, BenchText(";"), new UTF8Encoding(true)); lblBMsg.Text = "Salvo em " + sd.FileName; }
                catch (Exception ex) { lblBMsg.Text = "Falha ao salvar: " + ex.Message; }
            }
        }

        void BenchLock(bool run)
        {
            for (int i = 0; i < 5; i++) { bBtn[i].Enabled = !run || i == bKind; cbDurK[i].Enabled = !run; }
            cbBThrF.Enabled = !run; cbBThr.Enabled = !run; cbBFur.Enabled = !run; cbBRam.Enabled = !run; cbBDrive.Enabled = !run; cbBSize.Enabled = !run;
        }

        void ToggleBench(int pick)
        {
            if (bRunning) { StopBench("Interrompido pelo usuário."); return; }
            if (pick < 0)
            {
                if (!bSeqRun || bQueue.Count == 0) { bSeqRun = false; return; }
                pick = bQueue.Dequeue();
            }
            else bSeqRun = false;
            if (Bench.Alive) { lblBMsg.Text = "Finalizando o teste anterior... tente de novo em um instante."; return; }
            int kind = pick;
            int[] durs = { 30, 60, 300, 600, 0 };
            int di = cbDurK[kind].SelectedIndex; if (bSeqRun && di == 4) di = 1;
            bDurSec = durs[di];
            Bench.FurCount = FurSteps[Math.Max(0, Math.Min(FurSteps.Length - 1, cbBFur.SelectedIndex))];
            Settings.Set("benchFur", cbBFur.SelectedIndex.ToString());
            int cores = Environment.ProcessorCount;
            int thr = cbBThr.SelectedIndex == 0 ? cores : (cbBThr.SelectedIndex == 1 ? Math.Max(1, cores / 2) : 1);   // CPU e FPU compartilham
            int ramPct = new int[] { 25, 50, 70 }[cbBRam.SelectedIndex];
            int mb = new int[] { 512, 1024, 2048, 4096 }[cbBSize.SelectedIndex];
            string drive = cbBDrive.SelectedItem as string;
            if (kind == 4)
            {
                if (drive == null) { lblBMsg.Text = "Nenhum drive disponível para o teste de SSD."; return; }
                if (bDurSec == 0 && Dlg.Show("Em 'Até parar' o SSD recebe gravações contínuas (gera desgaste). Continuar?", "BunoTunner", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            BenchSave(kind); bGlChecked = false;
            string gname = cur != null ? cur.Name : null;
            bExt = kind <= 2; bU = BUnit[kind]; bSc = BScale[kind];
            if (kind == 0) { bU = "FPS"; bSc = 1.0; }
            bFallback = delegate { Bench.Start(kind, thr, ramPct, drive, mb, gname); };
            if (bExt)
            {
                Bench.Stop = false; Th.Interlocked.Exchange(ref Bench.Counter, 0); Th.Interlocked.Exchange(ref Bench.Errors, 0); Bench.Extra = ""; Bench.Fail = null;
            }
            else Bench.Start(kind, thr, ramPct, drive, mb, gname);
            bKind = kind; bRunning = true; bStart = DateTime.UtcNow; bLastT = bStart; bLast = 0;
            bSum = 0; bSq = 0; bPeak = 0; bTempMax = 0; bN = 0; bRateNow = null; bTempNow = null;

            bsnRate = new Sn(); bsnRate.Group = "Benchmark"; bsnRate.Name = "Taxa (" + bU + ")"; bsnRate.Type = "Data"; bsnRate.Get = delegate { return bRateNow; };
            bsnTemp = new Sn(); bsnTemp.Group = "Benchmark"; bsnTemp.Name = "Temperatura"; bsnTemp.Type = "Temperature"; bsnTemp.Get = delegate { return bTempNow; };
            grBench.Items.Clear();
            Series s1 = new Series(); s1.Sn = bsnRate; s1.Color = Color.FromArgb(0, 200, 0);
            Series s2 = new Series(); s2.Sn = bsnTemp; s2.Color = Color.FromArgb(255, 140, 0);
            grBench.Items.Add(s1); grBench.Items.Add(s2);

            BenchLock(true);
            try
            {
                if (rf != null && !rf.IsDisposed) rf.Close();
                rf = new RenderForm(kind, thr);
                rf.Cv.Unit = bU; rf.Cv.Total = bDurSec;
                rf.FormClosed += delegate { rf = null; if (bRunning) StopBench("Janela de render fechada: teste interrompido."); };
                rf.Show(this);
            }
            catch { rf = null; }
            bBtn[kind].Badge = 2; bBtn[kind].Invalidate(); btnBSeq.Text = "Parar tudo";
            pbBench.Marquee = bDurSec == 0; pbBench.Value = 0;
            lblBMsg.Text = "Rodando...";
            tmBench.Start();
        }

        float? BenchTemp(int kind)
        {
            if (kind == 0)
            {
                int? t = cur == null ? null : cur.Temp();
                return t.HasValue ? (float?)t.Value : null;
            }
            string pre = kind == 4 ? "Armazenamento" : "Processador";
            string[] names = kind == 4 ? new string[] { "" } : new string[] { "Package", "Tdie", "Tctl", "Core Max", "Core", "" };
            foreach (string nm in names)
                foreach (Sn s in SensorHub.PcSensors)
                    if (s.Group.StartsWith(pre) && s.Type == "Temperature" && s.IsRealTemp && s.Last.HasValue && s.Name.IndexOf(nm, StringComparison.OrdinalIgnoreCase) >= 0)
                        return s.Last;
            return null;
        }

        void BenchTick()
        {
            if (!bRunning) return;
            if (bExt && bKind == 0 && !bGlChecked && rf != null && rf.Gl != null && rf.Gl.State == 1 && rf.Gl.Renderer.Length > 0)
            {
                bGlChecked = true;
                string want = cur != null ? cur.Name : null;
                if (want != null && cmb.Items.Count > 1 && !Bench.GpuNameMatch(rf.Gl.Renderer, want))
                {
                    // o Windows escolhe a placa do OpenGL; para respeitar a placa selecionada, usa o OpenCL nela
                    string got = rf.Gl.Renderer;
                    rf.Gl.StopRender();
                    bExt = false; bU = BUnit[0]; bSc = BScale[0]; bLast = 0; bLastT = DateTime.UtcNow;
                    if (rf.Cv != null) rf.Cv.Unit = bU;
                    lblBMsg.Text = "O OpenGL abriu na placa '" + got + "', diferente da selecionada ('" + want + "'): usando o teste OpenCL na placa selecionada.";
                    bFallback();
                    return;
                }
            }
            if (bExt && bKind == 0 && rf != null && rf.Gl != null && rf.Gl.State == 2)
            {
                bExt = false; bU = BUnit[0]; bSc = BScale[0]; bLast = 0; bLastT = DateTime.UtcNow;
                if (rf.Cv != null) rf.Cv.Unit = bU;
                lblBMsg.Text = "OpenGL indisponível (" + rf.Gl.Error + "): usando o teste OpenCL.";
                bFallback();
                return;
            }
            DateTime now = DateTime.UtcNow;
            double dt = (now - bLastT).TotalSeconds;
            if (dt < 0.5) return;
            double elapsed = (now - bStart).TotalSeconds;
            long c = Th.Interlocked.Read(ref Bench.Counter);
            double rate = (c - bLast) / dt * bSc;
            bLast = c; bLastT = now;
            float? t = BenchTemp(bKind);
            bRateNow = (float)rate; bTempNow = t;
            bsnRate.Poll(); bsnTemp.Poll();
            foreach (Series se in grBench.Items)
            {
                if (!se.Sn.Last.HasValue) continue;
                se.V.Add(se.Sn.Last.Value);
                if (se.V.Count > XpGraph.Store) se.V.RemoveAt(0);
            }
            grBench.Invalidate();
            if (elapsed > 1.5) { bSum += rate; bSq += rate * rate; bN++; if (rate > bPeak) bPeak = rate; }

            string u = bU;
            long errs = Th.Interlocked.Read(ref Bench.Errors);
            if (rf != null && !rf.IsDisposed)
            {
                rf.Text = "BunoTunner - " + BName[bKind] + ": " + rate.ToString("0.0") + " " + bU + " (Esc fecha e para)"; rf.Invalidate(new Rectangle(0, 0, rf.Width, 32));
                rf.Cv.Rate = rate; rf.Cv.Temp = t.HasValue ? t.Value : double.NaN; rf.Cv.Elapsed = elapsed; rf.Cv.Errors = errs; rf.Cv.Extra = Bench.Extra;
            }
            lblBRate.Text = rate.ToString("0.0") + " " + u;
            lblBAvg.Text = bN > 0 ? (bSum / bN).ToString("0.0") + " " + u : "-";
            lblBPeak.Text = bPeak.ToString("0.0") + " " + u;
            if (t.HasValue) { bTempMax = Math.Max(bTempMax, t.Value); lblBTemp.Text = t.Value.ToString("0") + " \u00B0C  (max " + bTempMax.ToString("0") + ")"; }
            else lblBTemp.Text = "-";
            lblBErr.Text = errs.ToString(); lblBErr.ForeColor = errs > 0 ? Xp.Bad : Xp.Text;
            int es = (int)elapsed;
            lblBTime.Text = (es / 60).ToString("00") + ":" + (es % 60).ToString("00") + (bDurSec > 0 ? " / " + (bDurSec / 60).ToString("00") + ":" + (bDurSec % 60).ToString("00") + "   (restam " + Math.Max(0, bDurSec - es) + " s)" : "");
            string ex = Bench.Extra;
            if (!string.IsNullOrEmpty(ex)) lblBMsg.Text = ex;
            if (bDurSec > 0) pbBench.Value = (int)(100 * elapsed / bDurSec); else pbBench.Step();

            if (Bench.Fail != null) { StopBench("Falhou: " + Bench.Fail); return; }
            if (chkBSafe.Checked && t.HasValue && t.Value >= UI.Int(txBTemp, 95)) { StopBench("Parado por segurança: temperatura de " + t.Value.ToString("0") + " \u00B0C."); return; }
            if (bDurSec > 0 && elapsed >= bDurSec) { StopBench("Concluído."); return; }
            if (elapsed > 3 && !bExt && !Bench.Alive) { StopBench("Teste finalizado."); return; }
        }

        string BenchDelta()
        {
            if (bN < 1) return "-";
            double avg = bSum / bN;
            foreach (ListViewItem x in lvBench.Items)
                if (x.SubItems[0].Text == BName[bKind] && x.Tag is double)
                {
                    double pv = (double)x.Tag;
                    if (pv > 0) { double d = (avg - pv) / pv * 100; return (d >= 0 ? "+" : "") + d.ToString("0.0") + "%"; }
                }
            return "-";
        }

        string BenchStab()
        {
            if (bN < 3) return "-";
            double m = bSum / bN, vr2 = bSq / bN - m * m;
            if (m <= 0) return "-";
            double sd = Math.Sqrt(Math.Max(0, vr2));
            return Math.Max(0, 100 * (1 - sd / m)).ToString("0") + "%";
        }

        void StopBench(string msg)
        {
            bool wasRun = bRunning;
            bool okEnd = msg.StartsWith("Conclu");
            if (!okEnd && bSeqRun) { bSeqRun = false; bQueue.Clear(); msg += " (sequência cancelada)"; }
            Bench.Stop = true; bRunning = false; tmBench.Stop();
            try { if (rf != null && !rf.IsDisposed) rf.Close(); } catch { }
            rf = null;
            BenchLock(false);
            for (int bi = 0; bi < 5; bi++) { bBtn[bi].Badge = 1; bBtn[bi].Invalidate(); }
            btnBSeq.Text = bSeqRun && okEnd && bQueue.Count > 0 ? "Parar tudo" : "Rodar sequência (CPU, FPU, RAM, GPU)";
            pbBench.Marquee = false;
            double el = (DateTime.UtcNow - bStart).TotalSeconds;
            long errs = Th.Interlocked.Read(ref Bench.Errors);
            string u = bU;
            ListViewItem it = new ListViewItem(new string[] {
                BName[bKind], ((int)el) + " s",
                bN > 0 ? (bSum / bN).ToString("0.0") + " " + u : "-", bPeak.ToString("0.0") + " " + u,
                errs.ToString(), bTempMax > 0 ? bTempMax.ToString("0") : "-", BenchStab(), BenchDelta() });
            if (errs > 0) it.ForeColor = Xp.Bad;
            it.Tag = bN > 0 ? bSum / bN : 0.0;
            lvBench.Items.Insert(0, it);
            if (wasRun && msg.StartsWith("Conclu")) { try { System.Media.SystemSounds.Asterisk.Play(); } catch (Exception) { } }
            if (bKind == 1 && bN > 0) msg += "  Pontos CPU: " + (bSum / bN * 100).ToString("0") + " pts (" + (cbBThr.SelectedIndex == 2 ? "1 thread" : "multi") + ").";
            if (okEnd && bSeqRun)
            {
                if (bQueue.Count > 0)
                {
                    Timer nt = new Timer(); nt.Interval = 2500;
                    nt.Tick += delegate { nt.Stop(); nt.Dispose(); if (bSeqRun && !bRunning) ToggleBench(-1); };
                    nt.Start(); msg += "  Próximo teste em instantes...";
                }
                else { bSeqRun = false; msg += "  Sequência concluída."; }
            }
            lblBMsg.Text = msg + (errs > 0 ? "  ATENÇÃO: " + errs + " erro(s) de cálculo/verificação - sistema instável!" : "");
        }

        // ---------------- pagina de especificacoes da CPU ----------------
        Panel BuildCpuPage()
        {
            Panel p = new Panel(); p.Dock = DockStyle.Fill;
            XpGroup g = new XpGroup(); g.Title = "Especificações da CPU"; g.Dock = DockStyle.Fill; g.Padding = new Padding(10, 22, 10, 40);
            lvCpu.Dock = DockStyle.Fill; lvCpu.View = View.Details; lvCpu.FullRowSelect = true; lvCpu.GridLines = true;
            lvCpu.BackColor = Xp.EditBg; lvCpu.Font = Xp.Ui;
            lvCpu.Columns.Add("Propriedade", 250); lvCpu.Columns.Add("Valor", 380);
            lvCpu.Items.Add(new ListViewItem(new string[] { "Lendo informações da CPU...", "" }));
            g.Controls.Add(lvCpu);
            XpButton copy = new XpButton(); copy.Text = "Copiar tudo"; copy.SetBounds(10, 0, 110, 24);
            copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            g.Controls.Add(copy);
            XpButton look = new XpButton(); look.Text = "LookUp"; look.SetBounds(126, 0, 110, 24);
            look.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            g.Controls.Add(look);
            g.Resize += delegate { copy.Top = g.Height - 32; look.Top = copy.Top; };
            look.Click += delegate { Tpu.Open(CpuNameStr(), false, this); };
            copy.Click += delegate
            {
                StringBuilder sb = new StringBuilder();
                foreach (ListViewItem it in lvCpu.Items) sb.AppendLine(it.Text + ": " + it.SubItems[1].Text);
                try { Clipboard.SetText(sb.ToString()); } catch (Exception) { }
            };
            p.Controls.Add(g);
            return p;
        }

        static string CpuNameStr()
        {
            try { using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")) return Convert.ToString(k.GetValue("ProcessorNameString")).Trim(); }
            catch (Exception) { return ""; }
        }

        void FillCpu(List<string[]> rows)
        {
            lvCpu.BeginUpdate();
            lvCpu.Items.Clear(); cpuLive.Clear();
            foreach (string[] r in rows) AddSpec(lvCpu, r);
            ListViewItem sep = new ListViewItem(new string[] { "--- Ao vivo ---", "" });
            sep.ForeColor = Xp.GroupText;
            lvCpu.Items.Add(sep);
            string[] live = { "Uso total", "Temperatura", "Consumo do pacote", "Clock médio dos núcleos", "Leitura dos sensores da CPU" };
            foreach (string n in live)
            {
                ListViewItem it = new ListViewItem(new string[] { n, "-" });
                lvCpu.Items.Add(it); cpuLive.Add(it);
            }
            lvCpu.EndUpdate();
        }

        string CpuVal(string type, params string[] names)
        {
            foreach (string nm in names)
                foreach (Sn s in SensorHub.PcSensors)
                    if (s.Group.StartsWith("Processador") && s.Type == type && (type != "Temperature" || s.IsRealTemp) && s.Last.HasValue && s.Name.IndexOf(nm, StringComparison.OrdinalIgnoreCase) >= 0)
                        return s.Fmt(s.Last);
            return "-";
        }

        string CpuDiag()
        {
            bool drv = false, acpi = false;
            foreach (Sn s in SensorHub.PcSensors)
                if (s.Group.StartsWith("Processador") && s.Type == "Temperature" && s.Seen)
                {
                    if (s.Group.Contains("Zona térmica")) acpi = true; else drv = true;
                }
            if (drv) return "Ok - driver de sensores funcionando";
            if (acpi) return "Driver SEM leitura da CPU (Integridade de memória/Defender?) - usando zona térmica do Windows";
            return "Sem leitura de temperatura - veja o LEIA-ME (driver bloqueado?)";
        }

        void UpdateCpuLive()
        {
            if (cpuLive.Count != 5) return;
            cpuLive[4].SubItems[1].Text = CpuDiag();
            cpuLive[0].SubItems[1].Text = CpuVal("Load", "CPU Total");
            cpuLive[1].SubItems[1].Text = CpuVal("Temperature", "Package", "Tdie", "Tctl", "Core Max", "Core");
            cpuLive[2].SubItems[1].Text = CpuVal("Power", "Package");
            float sum = 0; int cnt = 0;
            foreach (Sn s in SensorHub.PcSensors)
                if (s.Group.StartsWith("Processador") && s.Type == "Clock" && s.Name.StartsWith("Core #") && s.Last.HasValue) { sum += s.Last.Value; cnt++; }
            cpuLive[3].SubItems[1].Text = cnt > 0 ? (sum / cnt).ToString("0") + " MHz" : "-";
        }

        // ---------------- troca de GPU ----------------
        void LoadGpu()
        {
            cur = cmb.SelectedItem as Gpu;
            if (cur == null) return;
            if (lblBGpu != null) lblBGpu.Text = cur.Name;
            fanPage.SetGpu(cur);

            LoadOc();
            lvSpec.Items.Clear();
            gpuGen++;
            foreach (string[] row in cur.Specs()) AddSpec(lvSpec, row);
            if (IsHandleCreated) StartGpuExtras();
        }

        // ---------------- loop de 1 segundo ----------------
        void Tick()
        {
            string at = SensorHub.AlertText;
            if (at != null) { if (status.Text != at) status.Text = at; status.ForeColor = Xp.Bad; }
            else { if (status.Text != statusText) status.Text = statusText; status.ForeColor = Xp.Text; }
            if (pgCpu.Visible) UpdateCpuLive();
            fanPage.Tick(pgFan.Visible);
            Gpu g = cur; if (g == null) return;
            if (pgOc.Visible)
            {
                float? clk = g.Clk(0, "Core"), sm = g.Clk(1, "Shader"), mem = g.Clk(2, "Memory"), vid = g.Clk(3, "Video");
                lblClocks.Text = "Núcleo: " + (clk.HasValue ? clk.Value.ToString("0") : "-") + " MHz     SM: " + (sm.HasValue ? sm.Value.ToString("0") : "-") +
                    " MHz     Memória: " + (mem.HasValue ? mem.Value.ToString("0") : "-") + " MHz     Video: " + (vid.HasValue ? vid.Value.ToString("0") : "-") + " MHz";
            }
        }

        // ---------------- iniciar com o Windows ----------------
        XpIconButton bWin, bUpd;
        void RefreshAutoBtn()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool on = AutoStart.IsOn();
                try { BeginInvoke(new Action(delegate { SetAutoBtn(on); })); } catch (Exception) { }
            });
        }

        // ---------------- atualizacao (GitHub Releases) ----------------
        const string UpdApi = "https://api.github.com/repos/Buno-o/BunoTunner/releases/latest";
        bool updBusy;
        void SetStatus(string s) { try { BeginInvoke(new Action(delegate { status.Text = s; })); } catch (Exception) { } }
        void CheckUpdate()
        {
            if (updBusy) return;
            updBusy = true; bUpd.Enabled = false;
            string oldStatus = status.Text;
            status.Text = Lang.T("Procurando atualizações...");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string tag = null, url = null, sha = null, err = null;
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                    HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(UpdApi);
                    rq.UserAgent = "BunoTunner"; rq.Timeout = 15000; rq.Accept = "application/vnd.github+json";
                    using (WebResponse rs = rq.GetResponse())
                    using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                    {
                        string js = sr.ReadToEnd();
                        Match m = Regex.Match(js, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                        if (m.Success) tag = m.Groups[1].Value;
                        m = Regex.Match(js, "\"browser_download_url\"\\s*:\\s*\"([^\"]*BunoTunner\\.exe)\"");
                        if (m.Success) url = m.Groups[1].Value;
                        m = Regex.Match(js, "\"name\"\\s*:\\s*\"BunoTunner\\.exe\".*?\"digest\"\\s*:\\s*\"sha256:([0-9a-fA-F]{64})\"", RegexOptions.Singleline);
                        if (m.Success) sha = m.Groups[1].Value;
                    }
                    if (tag == null || url == null) err = "A versão mais recente não foi encontrada.";
                }
                catch (Exception ex) { err = ex.Message; }
                try { BeginInvoke(new Action(delegate { UpdateAnswer(tag, url, sha, err, oldStatus); })); } catch (Exception) { }
            });
        }
        static Version VerOf(string s)
        {
            Version v; return Version.TryParse((s ?? "").Trim().TrimStart('v', 'V'), out v) ? v : new Version(0, 0);
        }
        void UpdateAnswer(string tag, string url, string sha, string err, string oldStatus)
        {
            status.Text = oldStatus; updBusy = false; bUpd.Enabled = true;
            if (err != null) { Dlg.Show(this, "Não foi possível verificar atualizações:\n" + err, "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (VerOf(tag) <= VerOf(Xp.Version)) { Dlg.Show(this, "Você já está na versão mais recente (" + Xp.Version + ").", "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (Dlg.Show(this, "Nova versão disponível: " + tag + " (atual: " + Xp.Version + ").\n\nDeseja baixar e atualizar agora?", "BunoTunner", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            updBusy = true; bUpd.Enabled = false;
            string cur = Application.ExecutablePath, tmp = cur + ".new";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string e2 = null;
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                    HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
                    rq.UserAgent = "BunoTunner"; rq.Timeout = 30000;
                    using (WebResponse rs = rq.GetResponse())
                    using (Stream ins = rs.GetResponseStream())
                    using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    {
                        long total = rs.ContentLength, got = 0; byte[] buf = new byte[81920]; int n; int lastPct = -1;
                        while ((n = ins.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n); got += n;
                            int pct = total > 0 ? (int)(got * 100 / total) : 0;
                            if (pct != lastPct) { lastPct = pct; SetStatus(Lang.T("Baixando atualização...") + " " + pct + "%"); }
                        }
                    }
                    FileInfo fi = new FileInfo(tmp);
                    byte[] head = new byte[2];
                    using (FileStream hs = File.OpenRead(tmp)) hs.Read(head, 0, 2);
                    if (fi.Length < 500000 || head[0] != 'M' || head[1] != 'Z') throw new Exception("O arquivo baixado e inválido.");
                    if (sha != null)
                    {
                        string h;
                        using (System.Security.Cryptography.SHA256 sh = System.Security.Cryptography.SHA256.Create())
                        using (FileStream hs = File.OpenRead(tmp)) h = BitConverter.ToString(sh.ComputeHash(hs)).Replace("-", "").ToLower();
                        if (h != sha.ToLower()) throw new Exception("O SHA-256 do arquivo baixado não confere com o publicado no GitHub; o arquivo foi descartado.");
                    }
                }
                catch (Exception ex) { e2 = ex.Message; try { File.Delete(tmp); } catch (Exception) { } }
                try { BeginInvoke(new Action(delegate { UpdateDone(e2, tag, cur, tmp, sha != null, oldStatus); })); } catch (Exception) { }
            });
        }
        void UpdateDone(string err, string tag, string cur, string tmp, bool hashOk, string oldStatus)
        {
            status.Text = oldStatus; updBusy = false; bUpd.Enabled = true;
            if (err != null) { Dlg.Show(this, "Falha ao baixar a atualização:\n" + err, "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string old = cur + ".old";
            try
            {
                if (File.Exists(old)) File.Delete(old);
                File.Move(cur, old);          // um .exe em uso pode ser renomeado
                File.Move(tmp, cur);
            }
            catch (Exception)
            {
                try { if (!File.Exists(cur) && File.Exists(old)) File.Move(old, cur); } catch (Exception) { }
                string alt = Path.Combine(Path.GetDirectoryName(cur), "BunoTunner_" + tag + ".exe");
                try { if (File.Exists(alt)) File.Delete(alt); File.Move(tmp, alt); } catch (Exception) { alt = tmp; }
                Dlg.Show(this, "A nova versão foi baixada, mas não foi possível substituir o arquivo atual.\nEla foi salva em:\n" + alt, "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (Dlg.Show(this, "Atualizado para " + tag + (hashOk ? " (SHA-256 conferido)." : ".") + "\n\nReiniciar o BunoTunner agora?", "BunoTunner", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                Process.Start(new ProcessStartInfo(cur, "--restart " + Process.GetCurrentProcess().Id));
            }
            catch (Exception) { }
            Close();
        }
        void SetAutoBtn(bool on)
        {
            bWin.Kind = on ? "win10on" : "win10"; bWin.Selected = on; bWin.Invalidate();
            tip.SetToolTip(bWin, on ? "Iniciar com o Windows: LIGADO\nClique para desligar." : "Iniciar com o Windows: desligado\nClique para ligar (abre na bandeja ao entrar no Windows).");
        }
        void ToggleAutoStart()
        {
            bool want = bWin.Kind != "win10on";
            bWin.Enabled = false;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                bool ok = AutoStart.Set(want, out err);
                bool now = AutoStart.IsOn();
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        bWin.Enabled = true; SetAutoBtn(now);
                        if (!ok) Dlg.Show(this, "Não foi possível alterar a inicialização com o Windows:\n" + err, "BunoTunner", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }));
                }
                catch (Exception) { }
            });
        }

        // ---------------- abrir na frente ----------------
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        // o Windows nao deixa um programa elevado (administrador) passar na frente de um nao elevado (ex.: Explorer); isto contorna
        void ForceFront()
        {
            try
            {
                uint pid; uint ft = GetWindowThreadProcessId(GetForegroundWindow(), out pid), me = GetCurrentThreadId();
                if (ft != me) AttachThreadInput(me, ft, true);
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                TopMost = true; TopMost = false;
                BringToFront(); Activate(); SetForegroundWindow(Handle);
                if (ft != me) AttachThreadInput(me, ft, false);
            }
            catch (Exception) { }
        }

        // ---------------- bandeja do sistema ----------------
        NotifyIcon tray; bool trayTold;
        void InitTray()
        {
            try
            {
                tray = new NotifyIcon();
                tray.Icon = Icon; tray.Text = "BunoTunner";
                ContextMenuStrip cm = new ContextMenuStrip();
                cm.Items.Add("Abrir BunoTunner", null, delegate { ShowFromTray(); });
                ToolStripMenuItem mi = new ToolStripMenuItem("Botão - (minimizar) envia para a bandeja");
                mi.CheckOnClick = true; mi.Checked = Settings.Get("tray", "1") == "1";
                mi.CheckedChanged += delegate { Settings.Set("tray", mi.Checked ? "1" : "0"); };
                cm.Items.Add(mi);
                cm.Items.Add(new ToolStripSeparator());
                cm.Items.Add("Sair", null, delegate { Close(); });
                Lang.Menu(cm);
                tray.ContextMenuStrip = cm;
                tray.DoubleClick += delegate { ShowFromTray(); };
                tray.Visible = true;
            }
            catch (Exception) { tray = null; }
        }
        // botao "-" da janela: vai para a bandeja (se a opcao estiver ligada); o X fecha o programa
        protected override void OnMinimizeClick()
        {
            if (tray == null || Settings.Get("tray", "1") != "1") { base.OnMinimizeClick(); return; }
            Hide();
            if (!trayTold)
            {
                trayTold = true;
                try { tray.ShowBalloonTip(2500, "BunoTunner", Lang.T("Continua rodando na bandeja. Clique duas vezes no ícone para abrir."), ToolTipIcon.Info); } catch (Exception) { }
            }
        }
        void ShowFromTray()
        {
            RefreshAutoBtn();
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void OnClosing(object s, FormClosingEventArgs e)
        {
            if (tray != null) { try { tray.Visible = false; tray.Dispose(); } catch (Exception) { } tray = null; }
            Bench.Stop = true;
            timer.Stop();
            try { fanPage.RestoreAll(); affPage.Shutdown(); } catch (Exception) { }
            if (sensForm != null && !sensForm.IsDisposed) sensForm.Close();
            SensorHub.Close();
        }
    }
}
