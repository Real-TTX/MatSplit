MatUi 0.7.0 (d579942f92)
Made by MatDesign local-20261021.
The UI toolkit of the Mat apps. Rules, documentation and live examples: the MatDesign site.

Copy these files into the app's static files. An app never loads them from another site (rule CODE-03); update by copying again.

  <html lang="en" data-app="yourapp">      data-app and data-theme belong on the html element
  <head>
    <script src="/matui.boot.js"></script>      applies the saved theme before the first paint
    <link rel="stylesheet" href="/matui.css" />
  </head>
  <body>
    the icon sprite, matui-icons.svg, inline once at the top of the body
    <script src="/matui.js"></script>           at the end of the body

An app sets one colour:  [data-app="yourapp"] { --mat-app-accent: #2f6feb; }
css/ holds the parts of matui.css (tokens, base, shell, controls, forms, lists, dialogs, layout, apps) for apps that load them one by one.

Versions (docs/VERSIONING.md on the MatDesign site):
  The version is in the first line of matui.css and in window.MatUi.version; matui.manifest.json lists every file with its SHA-256.
  CHANGELOG.md says what changed in each version. A version with a Breaking section needs a change in the app: its Migration section says what.
  Update by copying ALL the files again, never a few of them: the files of one version belong together.
  To see how an app's copy stands against the toolkit:  node tools/matui-check.js <folder of the app>
