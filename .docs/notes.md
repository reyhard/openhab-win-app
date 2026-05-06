* Flyout should be by default attached to right side
  * It should be configurable in settings where flyout is happening
  * It should be narrow panel (500px?) ![image-20260506092623166](D:\Source\Openhab\openhab-win-app\.docs\image-20260506092623166.png)
    * Similar to calendar flyout ![image-20260506092245952](D:\Source\Openhab\openhab-win-app\.docs\image-20260506092245952.png)
* Flyout should have nice smooth fly out animation
* Once connection is established, all available sitemaps should be queried and user should be able to select it from drop down list
  * Only user name front facing name should be visible - not internal name
  * In flyout, clicking i.e. on sitemap name should triggered selection of sitemap
* Add some icons - i.e. "Settings" should use cog icon instead of text, same with "Refresh" button
  * Open App should be also icon - perhaps and expand icon or something similar in top right corner
* openhab-icon.svg should be used as app icon
* Sections and buttons should follow closer windows design 
  * ![image-20260506092953619](D:\Source\Openhab\openhab-win-app\.docs\image-20260506092953619.png) 
  * icons on the left, then text, text of item and then widget
* It seems transforms of ui elements are not used
  * ![image-20260506093059936](D:\Source\Openhab\openhab-win-app\.docs\image-20260506093059936.png) vs ![image-20260506093119143](D:\Source\Openhab\openhab-win-app\.docs\image-20260506093119143.png) 
  * Correct state is "UNLOCKED", not "OFF". Sitemaps can transform item state to something different
* Few sitemap things are still not supported
  * Submenus (frames)
  * Selection
  * Icons
  * perhaps more?
* There should be settings option to follow windows color scheme
  * There should be also option to replace default openhab sitemaps icon with the ones from Windows 11