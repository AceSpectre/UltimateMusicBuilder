# UltimateMusicBuilder

## General guidance

Whilst working on this codebase keep the following ideas in mind, if what you have been asked to do or are going to do doesnt adhere to these ideas, looks for ways that you can follow them and suggest alternatives:
- Code should be clean and simple, you should prefer oneline solutions over something needlessly complex
- Always follow DRY - code that repeats itself should be abstracted to ensure the same thing isnt maintained in several places
- Generally follow YAGNI - you should fight for the smallest correct implementation of a feature
- Tests should be high quality - tests that run fast (unit tests) should be used frequently to assert that logic works at a function/file/module level -> its vital that the program has verifiability to ensure that agents are implementing logic correctly. Slower tests (integretion/e2e) should generally not change and only a few should exist to ensure that all functionality stacked together works as expected 
- Avoid sloppy behaviours - comments should be concise and only a few lines, if a paragraph long comment is required to explain a workaround the code is wrong and should be fixed. Don't use em-dashes anywhere, instead of using terms like "smoke tests" and "ground truth" use terms like "test suite" and "correctness"
- Build to throw away - sloppy code and standards are acceptable for experiments but sloppy code should be thrown away even if the experiment works to ensure it is implemented properly

## Hard rules

- Agents are not allowed to edit the contents of CLAUDE.md
- Plan files should not be pushed to remote and should be deleted once plan is implemented
- Copyrighted game assets and materials must never make it to remote

