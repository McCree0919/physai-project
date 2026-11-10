"""mlagents-learn with the requested torch device enforced; never edits installed trainer or task."""
import argparse
import datetime
import json
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument('--train-device', required=True, choices=['cpu', 'cuda:0']);parser.add_argument('--train-record', required=True)
    args, remaining = parser.parse_known_args()
    from mlagents.trainers.ppo.trainer import PPOTrainer
    from mlagents.trainers.learn import main as learn_main
    import torch
    # ML-Agents 1.1's CPU switch does not undo PyTorch 2.x's earlier global CUDA default (see LAYER3_PILOT_DIAGNOSIS 3.1).
    torch.set_default_device(args.train_device)
    record = Path(args.train_record);record.parent.mkdir(parents=True, exist_ok=True)
    advance_original = PPOTrainer.advance
    def advance(self):
        if not getattr(self, '_device_checked', False):
            devices = {str(p.device) for p in self.policy.actor.parameters()}
            if hasattr(self.optimizer, 'critic'):devices.update(str(p.device) for p in self.optimizer.critic.parameters())
            assert devices == {args.train_device}, devices
            with record.open('a', encoding='utf-8') as stream:
                stream.write(json.dumps(dict(kind='start', utc=datetime.datetime.now(datetime.timezone.utc).isoformat(), steps=int(self.get_step), parameter_devices=sorted(devices), torch_threads=torch.get_num_threads()))+'\n')
            self._device_checked = True
        return advance_original(self)
    PPOTrainer.advance = advance
    sys.argv = ['mlagents-learn', *remaining]
    learn_main()


if __name__ == '__main__':main()
