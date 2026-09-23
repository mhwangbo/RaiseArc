using System;
using System.IO;
using System.Linq;
using PrincessStudio.Core;
using PrincessStudio.Editor;
using PrincessStudio.Unity;
using RaiseArc.Unity;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RaiseArc.Editor
{
    public sealed partial class BasicGameSetup
    {
        [SerializeField] private NewGameDefinition newGame = new NewGameDefinition();
        [SerializeField] private GameProgression newProgression;
        [SerializeField] private string lastBuild = "";
        private GameScreenDefinition screenDraft;
        private int screenRevision;
        private string imageLocale = "";
        private string activityImageId = "";
        private Texture2D activityImage;

        [MenuItem("Window/RaiseArc/Make a game")]
        public static void MakeGame() => Open(Selection.activeObject as GameProjectAsset);
        private void OnEnable() { Undo.undoRedoPerformed += ReloadSetup; }
        private void OnDisable() { Undo.undoRedoPerformed -= ReloadSetup; }
        private void ReloadSetup() { screenDraft = null; CreateGUI(); }
        private void DrawSetup()
        {
            var root = rootVisualElement; var offset = root.Q<ScrollView>()?.scrollOffset ?? Vector2.zero;
            root.Clear(); minSize = new Vector2(560, 520);
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; scroll.style.paddingLeft = scroll.style.paddingRight = 16; root.Add(scroll);
            scroll.schedule.Execute(() => scroll.scrollOffset = offset);
            scroll.Add(new Label(T("Make your raising game", "나의 육성 게임 만들기")) { style = { fontSize = 23, marginTop = 12, marginBottom = 12 } });
            var projectField = new ObjectField(T("Open an existing Game Project", "기존 게임 프로젝트 열기")) { objectType = typeof(GameProjectAsset), allowSceneObjects = false, value = project };
            projectField.RegisterValueChangedCallback(e => { project = e.newValue as GameProjectAsset; screenDraft = null; background = character = null; lastBuild = ""; CreateGUI(); }); scroll.Add(projectField);
            status = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 12, color = new Color(.95f,.65f,.3f) } }; root.Add(status);
            void Button(string en, string ko, Action action) => scroll.Add(new UnityEngine.UIElements.Button(() => Run(action)) { text = T(en, ko) });
            if (project == null)
            {
                Field(scroll, "Game title", "게임 제목", newGame.title, x => newGame.title = x);
                Field(scroll, "Character name", "캐릭터 이름", newGame.characterName, x => newGame.characterName = x);
                Number(scroll, "Starting age", "시작 나이", newGame.startingAge, x => newGame.startingAge = x);
                Number(scroll, "Game duration (days)", "전체 기간 (일)", newGame.durationDays, x => newGame.durationDays = x);
                Number(scroll, "Starting money", "시작 소지금", newGame.startingMoney, x => newGame.startingMoney = x);
                Choice(scroll, "Progression", "진행 방식", new[] { T("Choose one activity at a time", "활동 하나씩 선택"), T("Plan several activities", "여러 활동을 일정으로 편성") }, (int)newProgression, x => newProgression = (GameProgression)x);
                Check(scroll, "Include a minimal starting game", "최소 시작 구성 포함", newGame.includeStarter, x => newGame.includeStarter = x);
                scroll.Add(new HelpBox(T("Starting content includes 3 editable activities, an invitation and 2 endings. At 42 days, seven-day activities finish in about 6 actions. Other durations use one-day activities when not divisible by 7. Uncheck for an empty content project; add activities and endings before playing.", "시작 구성은 수정 가능한 활동 3개·초대 사건·엔딩 2개입니다. 42일이면 7일 활동 약 6번으로 끝납니다. 7로 나누어지지 않는 기간은 1일 활동을 만듭니다. 체크를 끄면 빈 콘텐츠로 시작하며, 플레이 전에 활동·엔딩을 추가해야 합니다."), HelpBoxMessageType.Info));
                foreach (var stat in newGame.stats.ToArray())
                {
                    var row = new VisualElement(); scroll.Add(row);
                    Field(row, "Stat name", "능력치 이름", stat.name, x => stat.name = x);
                    Number(row, "Initial value", "초기값", stat.initial, x => stat.initial = x);
                    row.Add(new UnityEngine.UIElements.Button(() => { newGame.stats.Remove(stat); CreateGUI(); }) { text = T("Remove stat", "능력치 빼기") });
                }
                Button("Add stat", "능력치 추가", () => { newGame.stats.Add(new NewGameStat { name = T("New stat", "새 능력치") }); CreateGUI(); });
                Button("Create my game", "내 게임 만들기", () => { project = GameCreation.Create(newGame); screenDraft = new GameScreenDefinition { progression = newProgression }; screenRevision = 0; Selection.activeObject = project; CreateGUI(); });
                StudioText.ApplyFont(root); return;
            }
            var p = project.Read(); var saved = GameScreenAuthoring.Find(project);
            if (screenDraft == null) { screenDraft = saved != null ? saved.Read() : new GameScreenDefinition(); screenRevision = saved != null ? saved.Revision : 0; }
            scroll.Add(new Label(AssetDatabase.GetAssetPath(project)));
            scroll.Add(new Label(T("1 · Make the content yours", "1 · 내 게임 콘텐츠 작성")) { style = { fontSize = 19, marginTop = 16 } });
            scroll.Add(new HelpBox(T("Edit in Studio, then Apply changes / Save game settings. These buttons open the same Game Project. Stop a running game before editing; start a new session to use saved rule changes.", "Studio에서 편집한 뒤 변경 적용 / 게임 설정 저장을 누르세요. 아래 버튼은 같은 게임 프로젝트를 엽니다. 실행 중에는 Stop으로 멈춘 뒤 편집하고, 저장한 규칙은 새 게임 세션에서 확인하세요."), HelpBoxMessageType.Info));
            foreach (var page in new[] { "Overview", "Character", "Stats & states", "Activities", "Events", "Endings", "Localization" })
            { var target = page; Button("Edit " + page, StudioText.T(page) + " 편집", () => StudioWindow.OpenProject(project, target)); }
            Button("Create another game (keep this one)", "다른 게임 새로 만들기 (기존 게임 보존)", () => { project = null; screenDraft = null; background = character = null; CreateGUI(); });
            scroll.Add(new Label(T("2 · Choose your screen", "2 · 화면 구성 선택")) { style = { fontSize = 19, marginTop = 16 } });
            Choice(scroll, "Progression", "진행 방식", new[] { T("Activity selection", "활동 선택"), T("Schedule planning", "일정 편성") }, (int)screenDraft.progression, x => screenDraft.progression = (GameProgression)x);
            Choice(scroll, "Layout", "배치", new[] { T("Information on left", "정보 왼쪽"), T("Information on right", "정보 오른쪽"), T("Compact / stacked", "작은 화면 / 세로 배치") }, (int)screenDraft.layout, x => screenDraft.layout = (GameScreenLayout)x);
            Choice(scroll, "Style", "스타일", new[] { T("Twilight", "황혼"), T("Paper", "종이"), T("Forest", "숲") }, (int)screenDraft.theme, x => screenDraft.theme = (GameScreenTheme)x);
            Number(scroll, "Text size (14–32)", "글자 크기 (14~32)", screenDraft.fontSize, x => screenDraft.fontSize = x);
            Check(scroll, "Show character/background", "캐릭터·배경 표시", screenDraft.showStage, x => screenDraft.showStage = x);
            Check(scroll, "Show stats", "능력치 표시", screenDraft.showStats, x => screenDraft.showStats = x);
            Check(scroll, "Stats above date/money", "날짜·돈보다 능력치를 위에 표시", screenDraft.statsFirst, x => screenDraft.statsFirst = x);
            Check(scroll, "Show voice controls", "보이스 조절 표시", screenDraft.showVoiceControls, x => screenDraft.showVoiceControls = x);
            var filters = new Foldout { text = T("Displayed stats and activity categories (empty = all)", "표시할 능력치·활동 분류 (선택 없음 = 전체)") }; scroll.Add(filters);
            foreach (var stat in p.stats) { var id = stat.id; var name = p.translations.Find(t => t.key == stat.nameKey && t.locale == StudioText.Language)?.text ?? id; Check(filters, name, name, screenDraft.visibleStats.Contains(id), x => { screenDraft.visibleStats.Remove(id); if (x) screenDraft.visibleStats.Add(id); }); }
            foreach (var category in p.activities.Select(a => string.IsNullOrEmpty(a.categoryId) ? a.category.ToString() : a.categoryId).Distinct())
            { var id = category; Check(filters, category, StudioText.T(category), screenDraft.activityCategories.Contains(id), x => { screenDraft.activityCategories.Remove(id); if (x) screenDraft.activityCategories.Add(id); }); }
            var locales = new[] { T("Shared image", "공통 이미지") }.Concat(p.locales).ToArray();
            Choice(scroll, "Image language", "이미지 언어", locales, Math.Max(0, Array.IndexOf(p.locales.ToArray(), imageLocale) + 1), x => { imageLocale = x == 0 ? "" : p.locales[x - 1]; background = character = activityImage = null; CreateGUI(); });
            ImageField(scroll, T("Replace background (optional)", "배경 교체 (선택)"), background, x => background = x);
            ImageField(scroll, T("Replace character (optional)", "캐릭터 교체 (선택)"), character, x => character = x);
            Check(scroll, "Fill background (crop edges)", "배경을 채우기 (가장자리 잘림)", screenDraft.fillBackground, x => screenDraft.fillBackground = x);
            var scale = new Slider(T("Character size", "캐릭터 크기"), .25f, 2) { value = screenDraft.portraitScale }; scale.RegisterValueChangedCallback(e => screenDraft.portraitScale = e.newValue); scroll.Add(scale);
            scroll.Add(new HelpBox(T("No images selected? The first screen creates editable solid-color placeholder images in your game's Assets folder. Replacements are copied; source import settings stay unchanged. Empty image fields keep existing images. Full actor staging remains available in Studio.", "처음에 이미지를 고르지 않으면 게임의 Assets 폴더에 편집 가능한 단색 임시 이미지를 만듭니다. 교체 이미지는 복사하며 원본 임포트 설정은 바꾸지 않습니다. 빈 이미지 칸은 기존 그림을 유지합니다. 고급 인물 연출은 Studio에서 계속 편집할 수 있습니다."), HelpBoxMessageType.Info));
            var activityImages = new Foldout { text = T("Activity images", "활동 이미지") }; scroll.Add(activityImages);
            if (p.activities.Count > 0)
            {
                var names = p.activities.Select(a => p.translations.Find(t => t.key == a.nameKey && t.locale == StudioText.Language)?.text ?? a.id).ToArray();
                var index = Math.Max(0, p.activities.FindIndex(a => a.id == activityImageId)); activityImageId = p.activities[index].id;
                Choice(activityImages, "Activity", "활동", names, index, x => activityImageId = p.activities[x].id);
                ImageField(activityImages, T("Replace activity image", "활동 이미지 교체"), activityImage, x => activityImage = x);
            }
            Button("Apply to game / create screen", "게임에 적용 / 화면 만들기", ApplyToGame);
            Button("Reload saved screen settings", "저장된 화면 설정 다시 읽기", ReloadSetup);
            scroll.Add(new Label(T("3 · Play, check an ending, export", "3 · 플레이 · 엔딩 검사 · 실행 파일 만들기")) { style = { fontSize = 19, marginTop = 16 } });
            Button("Open game scene", "게임 Scene 열기", OpenGameScene);
            Button("Play my game", "내 게임 플레이", () => { ApplyToGame(); OpenGameScene(); EditorApplication.isPlaying = true; });
            Button("Stop current game", "현재 게임 중지", () => EditorApplication.isPlaying = false);
            Button("Check an ending", "엔딩에 갈 수 있나요?", () => StudioWindow.OpenProject(project, "Endings"));
            Button("Build Windows executable…", "Windows 실행 파일 만들기…", BuildWindows);
            if (!string.IsNullOrEmpty(lastBuild)) Button("Run the built game", "만든 실행 파일 실행", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(lastBuild) { UseShellExecute = true }));
            scroll.Add(new HelpBox(T("Studio Test play checks rules. Play my game opens the actual Game view. Build creates a separate Windows application. Player saves are separate from authored content; incompatible saves are kept and require a new game.", "Studio 테스트 플레이는 규칙 점검입니다. 내 게임 플레이는 실제 Game 뷰를 엽니다. 빌드는 별도 Windows 프로그램을 만듭니다. 플레이어 세이브는 제작 데이터와 별개이며, 호환되지 않는 세이브는 보존하고 새 게임을 시작해야 합니다."), HelpBoxMessageType.Info));
            StudioText.ApplyFont(root);
        }
        private void Run(Action action) { try { action(); } catch (Exception e) { status.text = e.Message; Debug.LogWarning("[RaiseArc] " + e.Message); } }
        private void Field(VisualElement parent, string en, string ko, string value, Action<string> set) { var f = new TextField(T(en, ko)) { value = value }; f.RegisterValueChangedCallback(e => set(e.newValue)); parent.Add(f); }
        private void Number(VisualElement parent, string en, string ko, int value, Action<int> set) { var f = new IntegerField(T(en, ko)) { value = value }; f.RegisterValueChangedCallback(e => set(e.newValue)); parent.Add(f); }
        private void Check(VisualElement parent, string en, string ko, bool value, Action<bool> set) { var f = new Toggle(T(en, ko)) { value = value }; f.RegisterValueChangedCallback(e => set(e.newValue)); parent.Add(f); }
        private void Choice(VisualElement parent, string en, string ko, string[] choices, int value, Action<int> set) { var f = new DropdownField(T(en, ko), choices.ToList(), value); f.RegisterValueChangedCallback(e => set(choices.ToList().IndexOf(e.newValue))); parent.Add(f); }
        private void ImageField(VisualElement parent, string label, Texture2D value, Action<Texture2D> set)
        {
            var f = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false, value = value };
            var preview = new Image { image = value, scaleMode = ScaleMode.ScaleToFit }; preview.style.height = value == null ? 0 : 120;
            f.RegisterValueChangedCallback(e => { var image = e.newValue as Texture2D; set(image); preview.image = image; preview.style.height = image == null ? 0 : 120; }); parent.Add(f); parent.Add(preview);
        }
    }
}
