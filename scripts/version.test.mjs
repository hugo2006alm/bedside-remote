import test from 'node:test';
import assert from 'node:assert/strict';
import { calculateRelease, compareVersions, parseVersion } from './version.mjs';

test('bootstrap eligible changes into an explicitly unstable initial version', () => {
  assert.equal(calculateRelease([], ['feat: universal remote']).version, '0.1.0-alpha.1');
});
test('documentation-only changes do not publish', () => {
  assert.equal(calculateRelease(['v0.1.0-alpha.1'], ['docs: improve setup']).publish, false);
});
test('fixes, features and breaking changes select distinct SemVer increments', () => {
  const tags = ['v0.2.3-beta.1'];
  assert.equal(calculateRelease(tags, ['fix(input): preserve accents']).version, '0.2.4-alpha.1');
  assert.equal(calculateRelease(tags, ['feat(queue): reorder']).version, '0.3.0-alpha.1');
  assert.equal(calculateRelease(tags, ['feat!: new protocol']).version, '1.0.0-alpha.1');
  assert.equal(calculateRelease(tags, ['refactor: transport\n\nBREAKING CHANGE: incompatibility']).version, '1.0.0-alpha.1');
});
test('highest impact commit wins independent of order', () => {
  assert.equal(calculateRelease(['v1.2.3'], ['feat: queue', 'fix: typo'], 'beta').version, '1.3.0-beta.1');
});
test('tags are sorted numerically and unrelated tags ignored', () => {
  assert.equal(calculateRelease(['v0.9.0', 'v0.10.0', 'random'], ['fix: x']).version, '0.10.1-alpha.1');
  assert(compareVersions(parseVersion('1.0.0'), parseVersion('1.0.0-beta.9')) > 0);
});
test('stable is explicit and manual publishing still increments', () => {
  assert.equal(calculateRelease(['v1.0.0'], [], 'stable', true).version, '1.0.1');
  assert.throws(() => calculateRelease([], ['feat: x'], 'nightly'));
});
