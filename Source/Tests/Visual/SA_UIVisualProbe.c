#ifdef ENABLE_DIAG
// Development fixture only: deliberately outside Addon and excluded from release packages.
// Copy into a development checkout only when an isolated CLI preview is needed.
class SA_UIVisualProbe
{
 protected static bool s_Started;
 protected static string s_Output;
 protected static int s_Attempts;
 static void Start()
 {
  if (s_Started || !System.GetCLIParam("saUiPreview", s_Output)) return;
  s_Started = true;
  Print("SA_UI_PREVIEW_STARTED");
  GetGame().GetCallqueue().CallLater(Show, 4000, false);
 }
 protected static void Show()
 {
  Print("SA_UI_PREVIEW_SHOW");
  SA_Client.eligible = true; SA_Client.active = true; SA_Client.players = true; SA_Client.kick = true; SA_Client.ban = true;
  SA_Status data = new SA_Status();
  SA_Player one = new SA_Player(); one.playerId = 1; one.name = "BLCA Player"; one.uuid = "12345678-1234-4321-8123-000000000001"; one.platform = "PC";
  SA_Player two = new SA_Player(); two.playerId = 2; two.name = "لاعب عربي"; two.uuid = "12345678-1234-4321-8123-000000000002"; two.platform = "PS5";
  data.players.Insert(one); data.players.Insert(two);
  SCR_JsonSaveContext save = new SCR_JsonSaveContext(); save.WriteValue("", data); SA_PlayersUI.Show(save.ExportToString());
  SA_PlayersUI.Click("Row1");
  Widget root = GetGame().GetWorkspace().FindAnyWidget("SA_Players");
  if (root)
  {
   EditBoxWidget.Cast(root.FindAnyWidget("Reason")).SetText("Test reason 120");
   EditBoxWidget.Cast(root.FindAnyWidget("Minutes")).SetText("60");
  }
  GetGame().GetCallqueue().CallLater(Capture, 2500, false);
 }
 protected static void Capture()
 {
  FileIO.MakeDirectory("$profile:ScreenShots");
  bool saved = System.MakeScreenshot("$profile:ScreenShots/ui-players.bmp");
  PrintFormat("SA_UI_SCREENSHOT=%1 attempt=%2", saved, s_Attempts);
  if (!saved && s_Attempts++ < 8) { GetGame().GetCallqueue().CallLater(Capture, 2500, false); return; }
  GetGame().GetCallqueue().CallLater(Finish, 1000, false);
 }
 protected static void Finish()
 {
  string stay;
  if (System.GetCLIParam("saUiStay", stay)) return;
  GameStateTransitions.RequestGameTerminateTransition();
 }
}
modded class MainMenuUI
{
 override void OnMenuOpen()
 {
  super.OnMenuOpen(); SA_UIVisualProbe.Start();
 }
}
#endif
