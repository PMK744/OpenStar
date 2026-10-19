import { useState } from "react";
import { Button, Circle, Paragraph, Separator, Text, TextArea, XStack, YStack } from "tamagui";

import { Badge, LinkButton, Message, monospace, Panel } from "../components";

import type { SetupProps, SetupStep } from "./setup.props";

function Instruction({ index, children }: { index: number; children: string }) {
  return (
    <XStack gap="$3" alignItems="flex-start">
      <Circle size={22} backgroundColor="$blue4" flexShrink={0}>
        <Text fontSize={12} fontWeight="800" color="$blue11">
          {index}
        </Text>
      </Circle>
      <Paragraph flex={1} fontSize={14} lineHeight={21} color="$color12">
        {children}
      </Paragraph>
    </XStack>
  );
}

const stepOrder: SetupStep[] = ["signIn", "paste", "done"];
const stepLabels: Record<SetupStep, string> = { signIn: "Sign in", paste: "Verify", done: "Done" };

function Steps({ step }: { step: SetupStep }) {
  const current = stepOrder.indexOf(step);

  return (
    <XStack alignItems="center" gap="$2" flexWrap="wrap">
      {stepOrder.map((name, index) => {
        const done = index < current;
        const active = index === current;
        return (
          <XStack key={name} alignItems="center" gap="$2">
            <Circle
              size={24}
              backgroundColor={done || active ? "$blue9" : "$color4"}
              borderWidth={active ? 2 : 0}
              borderColor="$blue7"
            >
              <Text fontSize={12} fontWeight="800" color={done || active ? "#fff" : "$color11"}>
                {done ? "✓" : index + 1}
              </Text>
            </Circle>
            <Text fontSize={13} fontWeight={active ? "700" : "500"} color={active ? "$color12" : "$color11"}>
              {stepLabels[name]}
            </Text>
            {index < stepOrder.length - 1 ? <Separator width={24} borderColor="$color6" /> : null}
          </XStack>
        );
      })}
    </XStack>
  );
}

export function SetupView({
  step,
  account,
  isLoading = false,
  isLoginReady = false,
  error,
  hasLoginHelper = false,
  onStartLogin,
  onSubmitRedirect,
  onReopenLogin,
  onUnlink,
}: SetupProps) {
  const [redirect, setRedirect] = useState("");
  const [pasteManually, setPasteManually] = useState(false);
  const waitForHelper = hasLoginHelper && !pasteManually;

  return (
    <YStack gap="$5">
      {account?.isLinked ? (
        <Panel
          title="Linked account"
          actions={
            <XStack alignItems="center" gap="$3">
              <Badge tone="success" dot>
                Linked
              </Badge>
              <Button size="$3" variant="outlined" theme="red" disabled={isLoading} onPress={onUnlink}>
                Unlink
              </Button>
            </XStack>
          }
          description={`${account.email ?? "GM account"} · ${account.vins.length} vehicle(s). Signing in again replaces these tokens.`}
        >
          <Message tone="error">{account.lastError}</Message>
        </Panel>
      ) : (
        <Steps step={step} />
      )}

      {step === "signIn" ? (
        <Panel
          title="1. Sign in with GM"
          actions={hasLoginHelper ? <Badge tone="success" dot>Helper ready</Badge> : undefined}
          description="OpenStar opens GM's own sign-in page in a popup window of your browser. Your password and verification code go straight to GM; OpenStar only receives the app redirect GM sends at the end."
        >
          {hasLoginHelper ? (
            <YStack gap="$3">
              <Instruction index={1}>Sign in and complete the email or text verification.</Instruction>
              <Instruction index={2}>
                The OpenStar login helper sends GM's final redirect back here and links the account. No developer tools, no pasting.
              </Instruction>
            </YStack>
          ) : (
            <YStack gap="$3">
              <Instruction index={1}>
                Install the OpenStar login helper (below) for the smoothest flow, or continue and copy the redirect by hand.
              </Instruction>
              <Instruction index={2}>Sign in and complete the email or text verification.</Instruction>
              <Instruction index={3}>
                GM then tries to open the myChevrolet app and the window looks stuck. That is expected; come back here.
              </Instruction>
            </YStack>
          )}
          <XStack>
            <Button
              theme="blue"
              disabled={isLoading || !isLoginReady}
              onPress={() => {
                setPasteManually(false);
                onStartLogin();
              }}
            >
              {isLoginReady ? "Open GM sign-in" : "Preparing…"}
            </Button>
          </XStack>
        </Panel>
      ) : null}

      {step === "signIn" && !hasLoginHelper ? (
        <Panel
          title="Install the login helper (recommended)"
          description="Browsers don't let a web page install extensions, so add it once by hand. Without it you'll copy GM's redirect from the browser's developer tools; with it, sign-in links the account on its own. This page detects the helper the moment it's installed."
          actions={
            <LinkButton size="$3" theme="blue" href="/login-helper.zip">
              Download helper (.zip)
            </LinkButton>
          }
        >
          <YStack gap="$3">
            <Instruction index={1}>
              Click "Download helper (.zip)" above, then unzip it to a folder you'll keep (the browser loads it from there).
            </Instruction>
            <Instruction index={2}>
              Chrome or Edge: open chrome://extensions, turn on Developer mode, click "Load unpacked", and select the unzipped folder.
            </Instruction>
            <Instruction index={3}>
              Firefox: open about:debugging, click "Load Temporary Add-on", and select manifest.json inside the unzipped folder.
            </Instruction>
            <Instruction index={4}>
              Open the extension's options, enter this OpenStar address, save, and allow access. This page then shows the helper is ready.
            </Instruction>
          </YStack>
        </Panel>
      ) : null}

      {step === "paste" && waitForHelper ? (
        <Panel
          title="2. Waiting for sign-in…"
          actions={<Badge tone="info" dot>Listening</Badge>}
          description="Finish signing in on the GM window. The login helper passes GM's redirect to this page and OpenStar links the account, then this page continues on its own."
        >
          <XStack gap="$3" flexWrap="wrap">
            <Button variant="outlined" disabled={isLoading} onPress={() => setPasteManually(true)}>
              Paste it manually instead
            </Button>
            <Button variant="outlined" disabled={isLoading} onPress={onReopenLogin}>
              Start over
            </Button>
          </XStack>
        </Panel>
      ) : null}

      {step === "paste" && !waitForHelper ? (
        <Panel
          title="2. Paste the redirect"
          description="Copy the msauth.com.gm.myChevrolet://auth?code=… address from the GM window and paste it below. It works once and expires within minutes."
        >
          <YStack gap="$3">
            <Instruction index={1}>
              Chrome or Edge: in the Console tab (F12), copy the URL from the "Failed to launch 'msauth.com.gm.myChevrolet://…'" message.
            </Instruction>
            <Instruction index={2}>
              Any browser: in the Network tab, select the last custlogin.gm.com request with status 302 and copy its Location response header.
            </Instruction>
          </YStack>
          <TextArea
            minHeight={110}
            placeholder="msauth.com.gm.myChevrolet://auth?state=…&code=…"
            value={redirect}
            onChangeText={setRedirect}
          />
          <XStack gap="$3" flexWrap="wrap">
            <Button theme="blue" disabled={!redirect.trim() || isLoading} onPress={() => onSubmitRedirect(redirect.trim())}>
              {isLoading ? "Linking…" : "Link account"}
            </Button>
            <Button variant="outlined" disabled={isLoading} onPress={onReopenLogin}>
              Start over
            </Button>
          </XStack>
        </Panel>
      ) : null}

      {step === "done" && account ? (
        <Panel
          title="3. All set"
          actions={<Badge tone="success" dot>Linked</Badge>}
          description="OpenStar refreshes the GM tokens on its own while it runs. Create an API key for each app that will call the API."
        >
          <YStack gap="$1.5">
            {account.vins.map((vin) => (
              <XStack key={vin} alignItems="center" gap="$2">
                <Text fontSize={13} color="$green11">
                  ✓
                </Text>
                <Text style={monospace} fontSize={14} color="$color12">
                  {vin}
                </Text>
              </XStack>
            ))}
          </YStack>
          <XStack gap="$3" flexWrap="wrap">
            <LinkButton theme="blue" href="/keys">
              Create an API key
            </LinkButton>
            <LinkButton href="/" variant="outlined">
              Open dashboard
            </LinkButton>
          </XStack>
        </Panel>
      ) : null}

      <Message tone="error">{error}</Message>
    </YStack>
  );
}
