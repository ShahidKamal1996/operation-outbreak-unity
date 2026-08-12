using OperationOutbreak.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace OperationOutbreak.UI
{
 [DisallowMultipleComponent] public sealed class GameOverController : MonoBehaviour
 {
  [SerializeField] private PlayerHealth playerHealth;
  [Tooltip("Milestone 1K - victory owner, used only to keep the two outcomes exclusive.")]
  [SerializeField] private MissionCompleteController missionComplete;
  private GameObject panel; private bool shown;
  // Milestone 1O - raised once, when the Game Over screen is actually shown (after the
  // victory-exclusion guard). Diagnostics uses it as the failure end-of-run checkpoint.
  public event System.Action GameOverShown;
  void Awake(){ Build(); }
  void OnEnable(){ if(playerHealth!=null) playerHealth.Died+=Show; }
  void OnDisable(){ if(playerHealth!=null) playerHealth.Died-=Show; }
  void Build(){
   var canvas=new GameObject("GameOverCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)); canvas.transform.SetParent(transform,false); var c=canvas.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=30; var sc=canvas.GetComponent<CanvasScaler>();sc.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;sc.referenceResolution=new Vector2(1080,1920);
   if(FindAnyObjectByType<EventSystem>()==null){var es=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));es.transform.SetParent(transform,false);}
   panel=new GameObject("GameOverPanel",typeof(RectTransform),typeof(Image));panel.transform.SetParent(canvas.transform,false);var r=(RectTransform)panel.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;panel.GetComponent<Image>().color=new Color(0,0,0,.72f);
   Text("GAME OVER",panel.transform,new Vector2(0,130),64);
   var b=new GameObject("RestartButton",typeof(RectTransform),typeof(Image),typeof(Button));b.transform.SetParent(panel.transform,false);var br=(RectTransform)b.transform;br.anchorMin=br.anchorMax=new Vector2(.5f,.5f);br.sizeDelta=new Vector2(340,110);br.anchoredPosition=new Vector2(0,-40);b.GetComponent<Image>().color=new Color(.2f,.75f,.32f,1);b.GetComponent<Button>().onClick.AddListener(Restart);Text("RESTART",b.transform,Vector2.zero,38);
   panel.SetActive(false);
  }
  static void Text(string value,Transform parent,Vector2 pos,float size){var go=new GameObject(value,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(700,100);r.anchoredPosition=pos;var t=go.GetComponent<TextMeshProUGUI>();t.font=TMP_Settings.defaultFontAsset;t.text=value;t.fontSize=size;t.fontStyle=FontStyles.Bold;t.alignment=TextAlignmentOptions.Center;t.color=Color.white;}
  // Milestone 1K - Game Over must never appear after victory. Zombies are already
  // suspended on victory so the Player cannot be damaged, this is a second safeguard.
  void Show(){if(shown)return; if(missionComplete!=null&&missionComplete.IsVictory)return; shown=true;panel.SetActive(true);GameOverShown?.Invoke();} void Restart(){SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);}
 }
}
