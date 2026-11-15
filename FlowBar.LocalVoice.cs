using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace FlowBar
{
    partial class Bar
    {
        sealed class LocalVoiceModel
        {
            public readonly string Id, Label, Revision, Digest;
            public readonly long Size;
            public LocalVoiceModel(string id, string label, string revision, string digest, long size)
            {
                Id = id; Label = label; Revision = revision; Digest = digest; Size = size;
            }
            public string FileName { get { return Id + "-Q8_0.gguf"; } }
        }

        static readonly LocalVoiceModel[] LocalVoiceModels =
        {
            new LocalVoiceModel("gigaam-v3-e2e-rnnt", "GigaAM v3 RNN-T · русский · 274 МБ", "3efd5b39882ceadca5570a8fb9fa1a2c801d6d6d", "78d63b47723b7f8d78c6113a6ef983b5a86e2a86f6c273e1f5cb6967b1c4467a", 273724832),
            new LocalVoiceModel("gigaam-v3-e2e-ctc", "GigaAM v3 CTC · русский · 272 МБ", "59717de63d50dd9dd93b3249da6ad3f2f1c4ad6b", "9ccce4750dc813a493d96ca15ee251712bedec15ac9a02fa3d2bd732f08ae5eb", 272151136),
            new LocalVoiceModel("whisper-small", "Whisper Small · несколько языков · 270 МБ", "4afdc7942ecf1997f359deae48a63506b062eac4", "9b9c8811bbcc82a7766f0fb0925614bdacb0923b2cc630daeac17108b655b860", 269751136)
        };

        const string LocalEngineTag = "0.4.2";
        const string LocalEnginePackage = "transcribe-native-windows-x86_64-cpu-vulkan";
        const string LocalEngineDigest = "6094102cf57b404b050707ceb258b12ce647b92e801b5d46077214e5b030c8aa";
        static bool localVoiceInstalling;
        static string LocalVoiceRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowBarSpeech"); } }
        static string LocalEnginePath { get { return Path.Combine(LocalVoiceRoot, "engine-" + LocalEngineTag, LocalEnginePackage); } }
        static bool LocalVoiceSupported { get { return Environment.Is64BitProcess && !IsArmWindows(); } }
        static LocalVoiceModel SelectedLocalVoice { get { return LocalVoiceModels.FirstOrDefault(model => model.Id == C("voice.model", "")) ?? LocalVoiceModels[0]; } }
        static string LocalModelPath(LocalVoiceModel model) { return Path.Combine(LocalVoiceRoot, "models", model.FileName); }
        static string HandyModelPath(LocalVoiceModel model) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.pais.handy", "models", model.FileName); }
        static bool ModelSizeMatches(string file, LocalVoiceModel model) { return File.Exists(file) && new FileInfo(file).Length == model.Size; }
        static bool LocalVoiceReady(LocalVoiceModel model)
        {
            return File.Exists(Path.Combine(LocalEnginePath, "transcribe.dll")) && ModelSizeMatches(LocalModelPath(model), model);
        }

        FrameworkElement LocalVoiceSetup()
        {
            var models = new ComboBox { ItemsSource = LocalVoiceModels.Select(m => m.Label).ToArray(), SelectedIndex = Array.IndexOf(LocalVoiceModels, SelectedLocalVoice) };
            System.Windows.Automation.AutomationProperties.SetName(models, "Модель распознавания речи");
            var status = CardStatus("");
            Border get = null;
            Action refresh = null;
            get = TaskButton("Скачать и включить", async () =>
            {
                if (DemoMode || !LocalVoiceSupported || localVoiceInstalling) return;
                var model = LocalVoiceModels[models.SelectedIndex];
                localVoiceInstalling = true; models.IsEnabled = get.IsEnabled = false;
                var progress = new Progress<string>(text => status.Text = text);
                try
                {
                    await PrepareLocalVoice(model, progress);
                    cfg["voice.model"] = model.Id; SaveCfg();
                    refresh();
                    status.Text = "Модель готова. Можно начинать запись.";
                }
                catch (Exception ex)
                {
                    Log(ex);
                    status.Text = "Не удалось подготовить модель. Проверьте интернет и место на диске, затем повторите.";
                }
                finally { localVoiceInstalling = false; models.IsEnabled = get.IsEnabled = true; }
            }, true);
            refresh = () =>
            {
                if (!LocalVoiceSupported) { status.Text = "Локальный движок сейчас доступен на Windows x64."; return; }
                var model = LocalVoiceModels[models.SelectedIndex];
                bool ready = LocalVoiceReady(model);
                ((TextBlock)get.Child).Text = ready ? "Включить модель" : "Скачать и включить";
                System.Windows.Automation.AutomationProperties.SetName(get, ((TextBlock)get.Child).Text);
                status.Text = ready ? (model == SelectedLocalVoice ? "Модель готова и выбрана." : "Модель скачана. Её можно включить.") :
                    ModelSizeMatches(HandyModelPath(model), model) ? "Найден файл модели Handy. FlowBar проверит и скопирует его." :
                    "Аудио распознаётся на этом компьютере. При первом запуске также загрузится движок (18 МБ).";
            };
            get.IsEnabled = LocalVoiceSupported && !DemoMode;
            models.SelectionChanged += (s, e) => refresh();
            refresh();
            StyleChoice(models, CardFg, TaskAccent);
            return TaskSurface(VS(CardHead("Модель речи"), models, status, CardActions(get)));
        }

        static string VoiceSha256(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static void FetchVoiceAsset(string url, string target, string digest, long maxBytes, string label, IProgress<string> progress)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "FlowBar";
            request.Timeout = request.ReadWriteTimeout = 30000;
            progress.Report("Скачиваем " + label + "…");
            using (var reply = request.GetResponse())
            using (var source = reply.GetResponseStream())
            using (var output = File.Create(target))
            {
                long total = reply.ContentLength, written = 0;
                int previous = -1;
                var buffer = new byte[81920];
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    written += count;
                    if (written > maxBytes) throw new InvalidDataException("Файл превышает ожидаемый размер.");
                    output.Write(buffer, 0, count);
                    int percent = total > 0 ? (int)(written * 100 / total) : 0;
                    if (percent / 5 != previous / 5 || written == total) progress.Report("Скачиваем " + label + " · " + percent + "%");
                    previous = percent;
                }
            }
            if (VoiceSha256(target) != digest) throw new InvalidDataException("Контрольная сумма " + label + " не совпала.");
        }

        static async Task PrepareLocalVoice(LocalVoiceModel model, IProgress<string> progress)
        {
            if (LocalVoiceReady(model)) return;
            Directory.CreateDirectory(LocalVoiceRoot);
            string staging = Path.Combine(LocalVoiceRoot, "download-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                if (!File.Exists(Path.Combine(LocalEnginePath, "transcribe.dll")))
                {
                    string archive = Path.Combine(staging, "engine.tar.gz");
                    string url = "https://github.com/handy-computer/transcribe.cpp/releases/download/v" + LocalEngineTag +
                        "/transcribe-native-" + LocalEngineTag + "-windows-x86_64-cpu-vulkan.tar.gz";
                    await Task.Run(() => FetchVoiceAsset(url, archive, LocalEngineDigest, 24 * 1024 * 1024, "движок", progress));
                    string unpack = Path.Combine(staging, "engine");
                    Directory.CreateDirectory(unpack);
                    progress.Report("Подготавливаем движок…");
                    await RunVoiceProcess(Path.Combine(Environment.SystemDirectory, "tar.exe"), new[] { "-xf", archive, "-C", unpack },
                        null, staging, TimeSpan.FromMinutes(2), CancellationToken.None);
                    if (!File.Exists(Path.Combine(unpack, LocalEnginePackage, "transcribe.dll"))) throw new InvalidDataException("Нет библиотеки распознавания.");
                    string destinationEngine = Path.GetDirectoryName(LocalEnginePath);
                    if (Directory.Exists(destinationEngine)) Directory.Move(destinationEngine, Path.Combine(staging, "incomplete-engine"));
                    Directory.Move(unpack, destinationEngine);
                }

                string destination = LocalModelPath(model);
                if (!ModelSizeMatches(destination, model))
                {
                    string candidate = Path.Combine(staging, model.FileName);
                    await Task.Run(() =>
                    {
                        var existing = HandyModelPath(model);
                        if (ModelSizeMatches(existing, model) && VoiceSha256(existing) == model.Digest)
                        {
                            progress.Report("Копируем проверенную модель Handy…");
                            File.Copy(existing, candidate);
                            if (VoiceSha256(candidate) != model.Digest) throw new InvalidDataException("Модель изменилась при копировании.");
                        }
                        else
                        {
                            string url = "https://huggingface.co/handy-computer/" + model.Id + "-gguf/resolve/" + model.Revision + "/" + model.FileName;
                            FetchVoiceAsset(url, candidate, model.Digest, model.Size, "модель", progress);
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        if (File.Exists(destination)) File.Replace(candidate, destination, null);
                        else File.Move(candidate, destination);
                    });
                }
            }
            finally { try { Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        static async Task<string> TranscribeLocally(LocalVoiceModel model, string wav, CancellationToken stop)
        {
            string dir = Path.Combine(Path.GetTempPath(), "FlowBarSpeech-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var files = await Task.Run(() => SliceVoiceWave(wav, dir, stop, 24), stop);
                if (files.Count == 0) return "";
                string json = await RunVoiceProcess(Assembly.GetExecutingAssembly().Location,
                    new[] { "--voice-local", LocalEnginePath, LocalModelPath(model), dir }, null, dir, TimeSpan.FromMinutes(35), stop);
                return new JavaScriptSerializer().Deserialize<string>(json).Trim();
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        const string TranscribeDll = "transcribe.dll";
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport(TranscribeDll, CallingConvention = CallingConvention.Cdecl)]
        static extern int transcribe_init_backends(byte[] directory);
        [DllImport(TranscribeDll, CallingConvention = CallingConvention.Cdecl)]
        static extern int transcribe_open(byte[] model, IntPtr loadParams, IntPtr sessionParams, out IntPtr session);
        [DllImport(TranscribeDll, CallingConvention = CallingConvention.Cdecl)]
        static extern int transcribe_run(IntPtr session, float[] pcm, int length, IntPtr runParams);
        [DllImport(TranscribeDll, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr transcribe_full_text(IntPtr session);
        [DllImport(TranscribeDll, CallingConvention = CallingConvention.Cdecl)]
        static extern void transcribe_session_free(IntPtr session);

        static byte[] VoiceUtf8(string text) { return Encoding.UTF8.GetBytes(text + "\0"); }

        static string VoiceNativeText(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return "";
            int length = 0;
            while (Marshal.ReadByte(pointer, length) != 0) length++;
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        static float[] ReadVoiceSlice(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                reader.BaseStream.Position = 16;
                int formatLength = reader.ReadInt32();
                if (formatLength < 16 || formatLength > 65536) throw new InvalidDataException("Неверный формат WAV.");
                var format = reader.ReadBytes(formatLength);
                if (BitConverter.ToUInt16(format, 2) != 1 || BitConverter.ToInt32(format, 4) != 16000 || BitConverter.ToUInt16(format, 14) != 16)
                    throw new InvalidDataException("Нужен PCM 16 кГц, моно.");
                reader.BaseStream.Position += formatLength & 1;
                if (reader.ReadUInt32() != 0x61746164) throw new InvalidDataException("В WAV нет звука.");
                int bytes = reader.ReadInt32();
                if (bytes < 0 || bytes % 2 != 0 || bytes > 24 * 32000) throw new InvalidDataException("Неверная длина WAV.");
                var pcm = new float[bytes / 2];
                for (int i = 0; i < pcm.Length; i++) pcm[i] = reader.ReadInt16() / 32768f;
                return pcm;
            }
        }

        internal static int RunLocalVoiceWorker(string[] args)
        {
            if (args.Length != 4) return 2;
            try
            {
                string directory = Path.GetFullPath(args[1]);
                Environment.SetEnvironmentVariable("TRANSCRIBE_BACKENDS", "cpu");
                if (LoadLibraryEx(Path.Combine(directory, TranscribeDll), IntPtr.Zero, 0x1100) == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                int status = transcribe_init_backends(VoiceUtf8(directory));
                if (status != 0) throw new InvalidOperationException("Инициализация движка: " + status);
                IntPtr session;
                status = transcribe_open(VoiceUtf8(args[2]), IntPtr.Zero, IntPtr.Zero, out session);
                if (status != 0) throw new InvalidOperationException("Загрузка модели: " + status);
                var text = new List<string>();
                try
                {
                    foreach (var file in Directory.GetFiles(args[3], "*.wav").OrderBy(path => path, StringComparer.Ordinal))
                    {
                        var pcm = ReadVoiceSlice(file);
                        if (pcm.Length == 0) continue;
                        status = transcribe_run(session, pcm, pcm.Length, IntPtr.Zero);
                        if (status != 0) throw new InvalidOperationException("Распознавание речи: " + status);
                        var result = VoiceNativeText(transcribe_full_text(session)).Trim();
                        if (result.Length > 0) text.Add(result);
                    }
                }
                finally { transcribe_session_free(session); }
                using (var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
                    stdout.WriteLine(new JavaScriptSerializer().Serialize(string.Join(Environment.NewLine, text)));
                return 0;
            }
            catch (Exception ex)
            {
                using (var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false))) stderr.WriteLine(ex.Message);
                return 3;
            }
        }
    }
}
