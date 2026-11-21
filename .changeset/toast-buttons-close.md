---
type: fix
"Avalloy.Desktop"
---

Toasts with buttons now close when you press one of them, after running its action, even if the action fails. Before, a button ran its action but left the toast on screen, so a toast that stays until answered never went away and its buttons seemed to do nothing.