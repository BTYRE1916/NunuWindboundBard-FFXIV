/* Pure release selection. Shared by the site and its validation fixtures. */
(function (root, factory) {
  const helpers = factory();
  if (typeof module === 'object' && module.exports) module.exports = helpers;
  else root.WindboundRelease = helpers;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  const base = 'https://github.com/BTYRE1916/NunuWindboundBard-FFXIV';
  const version = value => {
    const match = /^v?(\d+)\.(\d+)\.(\d+)(?:\.0)?$/.exec(String(value || ''));
    return match ? match.slice(1).map(Number).join('.') : null;
  };
  function selectInstaller(release) {
    if (!release || release.draft || release.prerelease) return null;
    const normalized = version(release.tag_name);
    if (!normalized || release.html_url !== base + '/releases/tag/' + encodeURIComponent(release.tag_name)) return null;
    const candidates = (release.assets || []).filter(asset => {
      const match = /^FFXIVMIDIPLAYER-BY-NUNU-v(\d+\.\d+\.\d+)\.zip$/.exec(asset.name || '');
      return match && version(match[1]) === normalized && asset.size > 0 &&
        asset.browser_download_url === base + '/releases/download/' + encodeURIComponent(release.tag_name) + '/' + asset.name;
    });
    return candidates.length === 1 ? candidates[0] : null;
  }
  return { selectInstaller, version };
});
