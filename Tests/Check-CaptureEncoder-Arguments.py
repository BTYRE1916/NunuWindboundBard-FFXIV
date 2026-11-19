from pathlib import Path
root=Path(__file__).resolve().parent.parent
pipeline=(root/'src/WindboundCaptureEngine/OutputPipeline.cs').read_text(encoding='utf-8')
assert '"-thread_queue_size"' not in pipeline, 'FFmpeg now interprets this as output-only: do not pass before -i inputs'
assert '"-i","pipe:0"' in pipeline, 'raw-video input missing'
assert '"-f","f32le"' in pipeline, 'PCM audio input missing'
build=(root/'Build-WindboundRelease.ps1').read_text(encoding='utf-8')
assert 'dotnet publish $engineProjectFile' in build, 'Build script does not publish patched engine'
assert 'Move-Item -LiteralPath $nestedFfmpeg' in build, 'Build script does not correct FFmpeg nesting'
print('PASS: Capture Studio encoder command avoids input-side thread_queue_size and package rebuilds engine.')
