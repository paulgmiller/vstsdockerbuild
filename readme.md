This is a fork of https://cloudbuild.visualstudio.com/CBT/_git/Template-VS2017-CloudBuild.
We have to fork so that coral has a repo in this account to use to start onboarding.

Differentces with cloudbuild version are.
`commit 5f3838fd293fdaa2d260db7eb0c60807ec4f8eb9`

We set cache settings so quickbuikld will use local caching.

```
commit 495d2ce02dfdf672fd5c8a60d1c5543cc45284bc
commit 9917a3003a1a9ed0d81933df8f699a70bee5604d
```

We have Have bootstrap scripts so that you know you actually have msbuild/nuget/cloudbuild on path and don't need to go into dev shell.