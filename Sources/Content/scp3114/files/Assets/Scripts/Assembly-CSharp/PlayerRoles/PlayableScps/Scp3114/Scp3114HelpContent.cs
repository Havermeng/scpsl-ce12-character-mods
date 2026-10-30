using UnityEngine;
using UnityEngine.UI;

namespace PlayerRoles.PlayableScps.Scp3114
{
    // Uses the original four translated descriptions and current key bindings.
    public sealed class Scp3114HelpContent : MonoBehaviour
    {
        private static readonly Scp3114HudTranslation[] Keys={Scp3114HudTranslation.HelpMenuSkeletonsInYourCLoset,Scp3114HudTranslation.HelpMenuCatchTheseHands,Scp3114HudTranslation.HelpMenuStrangulation,Scp3114HudTranslation.HelpMenuUnderTheirSkin};
        private static readonly ActionName[] Actions={ActionName.AltVoiceChat,ActionName.Shoot,ActionName.Zoom,ActionName.Interact};
        private void OnEnable()
        {
            for(int i=0;i<Keys.Length;i++)
            {
                var card=transform.Find("AbilityHelp"+i);
                if(card==null||!Translations.TryGet(Keys[i],out var value))continue;
                int split=value.IndexOf(':');
                if(split<0)continue;
                string title=value.Substring(0,split);
                string description=value.Substring(split+1);
                try{description=string.Format(description,NewInput.GetKey(Actions[i]));}
                catch(System.FormatException){continue;}
                description=System.Text.RegularExpressions.Regex.Replace(description,@"\bR\b",NewInput.GetKey(ActionName.Reload).ToString());
                var titleText=card.Find("Title").GetComponent<Text>();
                var bodyText=card.Find("Description").GetComponent<Text>();
                titleText.text=title.ToUpperInvariant();bodyText.text=description;
            }
        }
    }
}
