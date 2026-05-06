import { createPluginModule, createStepResult, defineStep } from "@cress/sdk";

export default createPluginModule({
  steps: [
    defineStep("Execute", async context => createStepResult({
      success: true,
      message: "sample plugin executed",
      outputs: {
        upper: context.inputs.text.toUpperCase()
      }
    }))
  ]
});
